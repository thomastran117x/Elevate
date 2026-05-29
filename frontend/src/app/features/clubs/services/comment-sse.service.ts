import { Injectable, NgZone } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import { PostComment, normalizePostComment } from '../models/club-post.types';

export type SseCommentEvent =
  | { type: 'CommentCreated'; comment: PostComment }
  | { type: 'CommentUpdated'; comment: PostComment }
  | { type: 'CommentDeleted'; postId: number; commentId: number };

@Injectable({ providedIn: 'root' })
export class CommentSseService {
  constructor(private zone: NgZone) {}

  connect(clubId: number, postId: number): Observable<SseCommentEvent> {
    const url = `${environment.backendUrl}/clubs/${clubId}/posts/${postId}/comments/events`;

    return new Observable<SseCommentEvent>((subscriber) => {
      const source = new EventSource(url);

      source.addEventListener('CommentCreated', (e: MessageEvent) => {
        this.zone.run(() => {
          try {
            const comment = normalizePostComment(JSON.parse(e.data));
            subscriber.next({ type: 'CommentCreated', comment });
          } catch {
            // malformed event — ignore
          }
        });
      });

      source.addEventListener('CommentUpdated', (e: MessageEvent) => {
        this.zone.run(() => {
          try {
            const comment = normalizePostComment(JSON.parse(e.data));
            subscriber.next({ type: 'CommentUpdated', comment });
          } catch {
            // malformed event — ignore
          }
        });
      });

      source.addEventListener('CommentDeleted', (e: MessageEvent) => {
        this.zone.run(() => {
          try {
            const payload = JSON.parse(e.data) as { postId: number; commentId: number };
            subscriber.next({
              type: 'CommentDeleted',
              postId: payload.postId,
              commentId: payload.commentId,
            });
          } catch {
            // malformed event — ignore
          }
        });
      });

      source.onerror = () => {
        // EventSource handles reconnection automatically; errors here are transient
      };

      return () => {
        source.close();
      };
    });
  }
}
