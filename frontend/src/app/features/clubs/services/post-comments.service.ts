import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { map, Observable } from 'rxjs';
import { environment } from '@environments/environment';
import { ApiEnvelope } from '../../../core/api/models/api-envelope.model';
import {
  PostComment,
  PostCommentApiResponse,
  PostCommentsApiResponse,
  normalizePostComment,
  normalizePostCommentsPagedData,
} from '../models/club-post.types';

@Injectable({ providedIn: 'root' })
export class PostCommentsService {
  constructor(private http: HttpClient) {}

  getComments(
    clubId: number,
    postId: number,
    page = 1,
    pageSize = 20,
  ): Observable<PostCommentsApiResponse> {
    const params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize));

    return this.http
      .get<ApiEnvelope<unknown>>(this.commentsUrl(clubId, postId), { params })
      .pipe(
        map((response) => {
          const raw = (response as ApiEnvelope<unknown> & { Data?: unknown }).data ??
            (response as { Data?: unknown }).Data ??
            null;
          return {
            ...response,
            data: raw ? normalizePostCommentsPagedData(raw as Parameters<typeof normalizePostCommentsPagedData>[0]) : null,
          } as PostCommentsApiResponse;
        }),
      );
  }

  createComment(
    clubId: number,
    postId: number,
    content: string,
  ): Observable<PostCommentApiResponse> {
    return this.http
      .post<ApiEnvelope<unknown>>(this.commentsUrl(clubId, postId), { content }, { withCredentials: true })
      .pipe(map((response) => this.normalizeCommentResponse(response)));
  }

  updateComment(
    clubId: number,
    postId: number,
    commentId: number,
    content: string,
  ): Observable<PostCommentApiResponse> {
    return this.http
      .put<ApiEnvelope<unknown>>(`${this.commentsUrl(clubId, postId)}/${commentId}`, { content }, { withCredentials: true })
      .pipe(map((response) => this.normalizeCommentResponse(response)));
  }

  deleteComment(
    clubId: number,
    postId: number,
    commentId: number,
  ): Observable<ApiEnvelope<null>> {
    return this.http.delete<ApiEnvelope<null>>(
      `${this.commentsUrl(clubId, postId)}/${commentId}`,
      { withCredentials: true },
    );
  }

  private commentsUrl(clubId: number, postId: number): string {
    return `${environment.backendUrl}/clubs/${clubId}/posts/${postId}/comments`;
  }

  private normalizeCommentResponse(response: ApiEnvelope<unknown>): PostCommentApiResponse {
    const raw = (response as ApiEnvelope<unknown> & { Data?: unknown }).data ??
      (response as { Data?: unknown }).Data ??
      null;
    return {
      ...response,
      data: raw ? normalizePostComment(raw as Parameters<typeof normalizePostComment>[0]) : null,
    } as PostCommentApiResponse;
  }
}
