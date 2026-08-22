using backend.main.features.clubs.realtime.contracts.responses;

namespace backend.main.features.clubs.realtime;

/// <inheritdoc cref="IClubPresenceStore"/>
public sealed class ClubPresenceStore : IClubPresenceStore
{
    /// <summary>How many roster entries are put on the wire before falling back to a bare count.</summary>
    public const int MaxRosterUsers = 50;

    /// <summary>
    /// How long a typing entry survives without a refresh. Clients re-send every ~2s while
    /// the composer is active, so this absorbs one missed refresh before clearing.
    /// </summary>
    public static readonly TimeSpan TypingTtl = TimeSpan.FromSeconds(5);

    private sealed class MemberEntry
    {
        public required PresenceUser User
        {
            get; set;
        }
        public HashSet<string> Connections { get; } = [];
    }

    private sealed record TypingEntry(PresenceUser User, DateTimeOffset ExpiresAt);

    // One lock rather than concurrent collections: every mutation here is a compound
    // read-modify-write (add member, then add connection; remove connection, then maybe
    // remove member) where lock-free removal would race a concurrent join and drop a user
    // who is still online. All operations are microseconds on small collections.
    private readonly object _gate = new();

    private readonly Dictionary<int, Dictionary<int, MemberEntry>> _clubs = [];
    private readonly Dictionary<string, HashSet<int>> _connectionClubs = [];
    private readonly Dictionary<string, Dictionary<string, TypingEntry>> _typing = [];
    private readonly Dictionary<string, HashSet<string>> _connectionThreads = [];

    public bool JoinClub(int clubId, string connectionId, PresenceUser? user)
    {
        lock (_gate)
        {
            if (!_connectionClubs.TryGetValue(connectionId, out var clubs))
            {
                clubs = [];
                _connectionClubs[connectionId] = clubs;
            }
            clubs.Add(clubId);

            // Anonymous viewers receive events but are never listed in the roster.
            if (user is null)
                return false;

            if (!_clubs.TryGetValue(clubId, out var members))
            {
                members = [];
                _clubs[clubId] = members;
            }

            if (members.TryGetValue(user.UserId, out var existing))
            {
                // Refresh the cached display info: a rename between tabs should not stick.
                existing.User = user;
                return existing.Connections.Add(connectionId) && existing.Connections.Count == 1;
            }

            var entry = new MemberEntry { User = user };
            entry.Connections.Add(connectionId);
            members[user.UserId] = entry;
            return true;
        }
    }

    public bool LeaveClub(int clubId, string connectionId, out PresenceUser? user)
    {
        lock (_gate)
        {
            user = null;

            if (_connectionClubs.TryGetValue(connectionId, out var clubs))
            {
                clubs.Remove(clubId);
                if (clubs.Count == 0)
                    _connectionClubs.Remove(connectionId);
            }

            if (!_clubs.TryGetValue(clubId, out var members))
                return false;

            var owner = members.FirstOrDefault(pair => pair.Value.Connections.Contains(connectionId));
            if (owner.Value is null)
                return false;

            owner.Value.Connections.Remove(connectionId);
            if (owner.Value.Connections.Count > 0)
                return false;

            user = owner.Value.User;
            members.Remove(owner.Key);
            if (members.Count == 0)
                _clubs.Remove(clubId);
            return true;
        }
    }

    public IReadOnlyList<int> ClubsFor(string connectionId)
    {
        lock (_gate)
        {
            return _connectionClubs.TryGetValue(connectionId, out var clubs)
                ? [.. clubs]
                : [];
        }
    }

    public PresenceSnapshot Snapshot(int clubId)
    {
        lock (_gate)
        {
            if (!_clubs.TryGetValue(clubId, out var members) || members.Count == 0)
                return new PresenceSnapshot(clubId, [], 0);

            var users = members.Values
                .Take(MaxRosterUsers)
                .Select(entry => entry.User)
                .ToList();
            return new PresenceSnapshot(clubId, users, members.Count);
        }
    }

    public void JoinThread(string connectionId, string threadKey)
    {
        lock (_gate)
        {
            if (!_connectionThreads.TryGetValue(connectionId, out var threads))
            {
                threads = [];
                _connectionThreads[connectionId] = threads;
            }
            threads.Add(threadKey);
        }
    }

    public bool LeaveThread(string connectionId, string threadKey)
    {
        lock (_gate)
        {
            if (_connectionThreads.TryGetValue(connectionId, out var threads))
            {
                threads.Remove(threadKey);
                if (threads.Count == 0)
                    _connectionThreads.Remove(connectionId);
            }

            return RemoveTypingEntry(threadKey, connectionId);
        }
    }

    public bool IsInThread(string connectionId, string threadKey)
    {
        lock (_gate)
        {
            return _connectionThreads.TryGetValue(connectionId, out var threads)
                && threads.Contains(threadKey);
        }
    }

    public IReadOnlyList<string> ThreadsFor(string connectionId)
    {
        lock (_gate)
        {
            return _connectionThreads.TryGetValue(connectionId, out var threads)
                ? [.. threads]
                : [];
        }
    }

    public bool SetTyping(
        string threadKey, string connectionId, PresenceUser user, bool isTyping, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!isTyping)
                return RemoveTypingEntry(threadKey, connectionId);

            if (!_typing.TryGetValue(threadKey, out var entries))
            {
                entries = [];
                _typing[threadKey] = entries;
            }

            // A refresh from a connection already listed keeps the roster identical, so it
            // must not rebroadcast — otherwise every throttle tick fans out to the thread.
            var alreadyTyping = entries.ContainsKey(connectionId)
                || entries.Values.Any(entry => entry.User.UserId == user.UserId);
            entries[connectionId] = new TypingEntry(user, now + TypingTtl);
            return !alreadyTyping;
        }
    }

    public IReadOnlyList<string> ExpireTyping(DateTimeOffset now)
    {
        lock (_gate)
        {
            List<string>? changed = null;

            foreach (var (threadKey, entries) in _typing)
            {
                var stale = entries
                    .Where(pair => pair.Value.ExpiresAt <= now)
                    .Select(pair => pair.Key)
                    .ToList();
                if (stale.Count == 0)
                    continue;

                var before = DistinctUserIds(entries);
                foreach (var connectionId in stale)
                    entries.Remove(connectionId);

                // Another tab of the same user may still be typing, which is not a change.
                if (before != DistinctUserIds(entries))
                    (changed ??= []).Add(threadKey);
            }

            foreach (var threadKey in _typing.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key).ToList())
                _typing.Remove(threadKey);

            return changed ?? (IReadOnlyList<string>)[];
        }
    }

    public ThreadTypingSnapshot Typing(string threadKey)
    {
        lock (_gate)
        {
            if (!_typing.TryGetValue(threadKey, out var entries) || entries.Count == 0)
                return new ThreadTypingSnapshot(threadKey, []);

            var users = entries.Values
                .GroupBy(entry => entry.User.UserId)
                .Select(group => group.First().User)
                .ToList();
            return new ThreadTypingSnapshot(threadKey, users);
        }
    }

    /// <summary>Must be called under <see cref="_gate"/>.</summary>
    private bool RemoveTypingEntry(string threadKey, string connectionId)
    {
        if (!_typing.TryGetValue(threadKey, out var entries) || !entries.ContainsKey(connectionId))
            return false;

        var before = DistinctUserIds(entries);
        entries.Remove(connectionId);
        if (entries.Count == 0)
            _typing.Remove(threadKey);

        return before != DistinctUserIds(entries);
    }

    private static int DistinctUserIds(Dictionary<string, TypingEntry> entries) =>
        entries.Values.Select(entry => entry.User.UserId).Distinct().Count();
}
