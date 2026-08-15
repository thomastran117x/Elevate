import { HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';

import { environment } from '@environments/environment';
import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';
import { ApiClient } from '../../../core/api/services/api-client.service';
import {
  DiscussionReplyApiResponse,
  DiscussionReplyPageApiResponse,
  DiscussionReplyReaction,
  DiscussionReplyReactionApiResponse,
  DiscussionReplySort,
  normalizeDiscussionReply,
  normalizeDiscussionReplyPage,
  normalizeDiscussionReplyReaction,
} from '../models/club-discussion.types';

@Injectable({ providedIn: 'root' })
export class DiscussionRepliesService {
  private readonly base = `${environment.backendUrl}/clubs`;

  constructor(private api: ApiClient) {}

  getReplies(
    clubId: number,
    discussionId: number,
    parentReplyId: number | null,
    sort: DiscussionReplySort,
    cursor: string | null = null,
    pageSize = 20,
  ): Observable<DiscussionReplyPageApiResponse> {
    let params = new HttpParams().set('sort', sort).set('pageSize', String(pageSize));
    if (parentReplyId !== null) params = params.set('parentReplyId', String(parentReplyId));
    if (cursor) params = params.set('cursor', cursor);
    return this.api
      .get<ApiEnvelope<unknown>>(this.repliesUrl(clubId, discussionId), {
        params,
        withCredentials: true,
      })
      .pipe(
        map((response) => ({
          ...response,
          data: this.rawData(response)
            ? normalizeDiscussionReplyPage(
                this.rawData(response) as Parameters<typeof normalizeDiscussionReplyPage>[0],
              )
            : null,
        })),
      ) as Observable<DiscussionReplyPageApiResponse>;
  }

  createReply(
    clubId: number,
    discussionId: number,
    content: string,
    parentReplyId: number | null,
  ): Observable<DiscussionReplyApiResponse> {
    return this.api
      .post<ApiEnvelope<unknown>>(
        this.repliesUrl(clubId, discussionId),
        { content, parentReplyId },
        { withCredentials: true },
      )
      .pipe(map((response) => this.mapReply(response)));
  }

  updateReply(
    clubId: number,
    discussionId: number,
    replyId: number,
    content: string,
  ): Observable<DiscussionReplyApiResponse> {
    return this.api
      .put<ApiEnvelope<unknown>>(
        `${this.repliesUrl(clubId, discussionId)}/${replyId}`,
        { content },
        { withCredentials: true },
      )
      .pipe(map((response) => this.mapReply(response)));
  }

  deleteReply(
    clubId: number,
    discussionId: number,
    replyId: number,
  ): Observable<DiscussionReplyApiResponse> {
    return this.api
      .delete<ApiEnvelope<unknown>>(`${this.repliesUrl(clubId, discussionId)}/${replyId}`, {
        withCredentials: true,
      })
      .pipe(map((response) => this.mapReply(response)));
  }

  setReaction(
    clubId: number,
    discussionId: number,
    replyId: number,
    reaction: DiscussionReplyReaction,
  ): Observable<DiscussionReplyReactionApiResponse> {
    return this.api
      .put<ApiEnvelope<unknown>>(
        `${this.repliesUrl(clubId, discussionId)}/${replyId}/reaction`,
        { reaction },
        { withCredentials: true },
      )
      .pipe(map((response) => this.mapReaction(response)));
  }

  clearReaction(
    clubId: number,
    discussionId: number,
    replyId: number,
  ): Observable<DiscussionReplyReactionApiResponse> {
    return this.api
      .delete<ApiEnvelope<unknown>>(
        `${this.repliesUrl(clubId, discussionId)}/${replyId}/reaction`,
        { withCredentials: true },
      )
      .pipe(map((response) => this.mapReaction(response)));
  }

  private repliesUrl(clubId: number, discussionId: number): string {
    return `${this.base}/${clubId}/discussions/${discussionId}/replies`;
  }

  private mapReply(response: ApiEnvelope<unknown>): DiscussionReplyApiResponse {
    const raw = this.rawData(response);
    return {
      ...response,
      data: raw
        ? normalizeDiscussionReply(raw as Parameters<typeof normalizeDiscussionReply>[0])
        : null,
    } as DiscussionReplyApiResponse;
  }

  private mapReaction(response: ApiEnvelope<unknown>): DiscussionReplyReactionApiResponse {
    const raw = this.rawData(response);
    return {
      ...response,
      data: raw
        ? normalizeDiscussionReplyReaction(
            raw as Parameters<typeof normalizeDiscussionReplyReaction>[0],
          )
        : null,
    } as DiscussionReplyReactionApiResponse;
  }

  private rawData(response: ApiEnvelope<unknown>): unknown {
    return (
      (response as ApiEnvelope<unknown> & { Data?: unknown }).data ??
      (response as { Data?: unknown }).Data ??
      null
    );
  }
}
