import { HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';

import { environment } from '@environments/environment';
import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';
import { ApiClient } from '../../../core/api/services/api-client.service';
import {
  normalizePostComment,
  normalizePostCommentReaction,
  normalizePostCommentsPagedData,
  PostCommentApiResponse,
  PostCommentReaction,
  PostCommentReactionApiResponse,
  PostCommentsApiResponse,
  PostCommentSort,
} from '../models/club-post.types';

@Injectable({ providedIn: 'root' })
export class PostCommentsService {
  private readonly base = `${environment.backendUrl}/clubs`;

  constructor(private api: ApiClient) {}

  getComments(
    clubId: number,
    postId: number,
    parentCommentId: number | null,
    sort: PostCommentSort,
    cursor: string | null = null,
    pageSize = 20,
  ): Observable<PostCommentsApiResponse> {
    let params = new HttpParams().set('sort', sort).set('pageSize', String(pageSize));
    if (parentCommentId !== null) {
      params = params.set('parentCommentId', String(parentCommentId));
    }
    if (cursor) params = params.set('cursor', cursor);
    return this.api
      .get<ApiEnvelope<unknown>>(this.commentsUrl(clubId, postId), {
        params,
        withCredentials: true,
      })
      .pipe(
        map((response) => ({
          ...response,
          data: this.rawData(response)
            ? normalizePostCommentsPagedData(
                this.rawData(response) as Parameters<typeof normalizePostCommentsPagedData>[0],
              )
            : null,
        })),
      ) as Observable<PostCommentsApiResponse>;
  }

  createComment(
    clubId: number,
    postId: number,
    content: string,
    parentCommentId: number | null,
  ): Observable<PostCommentApiResponse> {
    return this.api
      .post<ApiEnvelope<unknown>>(
        this.commentsUrl(clubId, postId),
        { content, parentCommentId },
        { withCredentials: true },
      )
      .pipe(map((response) => this.mapComment(response)));
  }

  updateComment(
    clubId: number,
    postId: number,
    commentId: number,
    content: string,
  ): Observable<PostCommentApiResponse> {
    return this.api
      .put<ApiEnvelope<unknown>>(
        `${this.commentsUrl(clubId, postId)}/${commentId}`,
        { content },
        { withCredentials: true },
      )
      .pipe(map((response) => this.mapComment(response)));
  }

  deleteComment(
    clubId: number,
    postId: number,
    commentId: number,
  ): Observable<PostCommentApiResponse> {
    return this.api
      .delete<ApiEnvelope<unknown>>(`${this.commentsUrl(clubId, postId)}/${commentId}`, {
        withCredentials: true,
      })
      .pipe(map((response) => this.mapComment(response)));
  }

  setReaction(
    clubId: number,
    postId: number,
    commentId: number,
    reaction: PostCommentReaction,
  ): Observable<PostCommentReactionApiResponse> {
    return this.api
      .put<ApiEnvelope<unknown>>(
        `${this.commentsUrl(clubId, postId)}/${commentId}/reaction`,
        { reaction },
        { withCredentials: true },
      )
      .pipe(map((response) => this.mapReaction(response)));
  }

  clearReaction(
    clubId: number,
    postId: number,
    commentId: number,
  ): Observable<PostCommentReactionApiResponse> {
    return this.api
      .delete<ApiEnvelope<unknown>>(`${this.commentsUrl(clubId, postId)}/${commentId}/reaction`, {
        withCredentials: true,
      })
      .pipe(map((response) => this.mapReaction(response)));
  }

  private commentsUrl(clubId: number, postId: number): string {
    return `${this.base}/${clubId}/posts/${postId}/comments`;
  }

  private mapComment(response: ApiEnvelope<unknown>): PostCommentApiResponse {
    const raw = this.rawData(response);
    return {
      ...response,
      data: raw ? normalizePostComment(raw as Parameters<typeof normalizePostComment>[0]) : null,
    } as PostCommentApiResponse;
  }

  private mapReaction(response: ApiEnvelope<unknown>): PostCommentReactionApiResponse {
    const raw = this.rawData(response);
    return {
      ...response,
      data: raw
        ? normalizePostCommentReaction(raw as Parameters<typeof normalizePostCommentReaction>[0])
        : null,
    } as PostCommentReactionApiResponse;
  }

  private rawData(response: ApiEnvelope<unknown>): unknown {
    return (
      (response as ApiEnvelope<unknown> & { Data?: unknown }).data ??
      (response as { Data?: unknown }).Data ??
      null
    );
  }
}
