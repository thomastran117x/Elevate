import { HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, from, map, switchMap } from 'rxjs';

import { ApiEnvelope, requireEnvelopeData } from '../../../core/api/models/api-envelope.model';
import {
  UnknownRecord,
  asRecord,
  readBoolean,
  readNullableString,
  readNumber,
  readString,
} from '../../../core/models/payload-casing';
import { ApiClient } from '../../../core/api/services/api-client.service';
import { AuthTokenService } from '../../../core/api/services/auth-token.service';
import { environment } from '../../../../environments/environment';

/**
 * One entry of the signed-in user's security history.
 *
 * Every classification is a snake_case name from a closed set on the server, never an ordinal, so
 * the view maps names it knows and falls back to a generic label for one added later.
 */
export interface AccountSecurityEvent {
  id: number;
  /** What happened, e.g. `signed_in` or `password_changed`. */
  kind: string;
  occurredAt: string;
  /** The sign-in method or linked provider, e.g. `password` or `google`. */
  method: string | null;
  /** The factor an MFA change was about, `sms` or `totp`. */
  factor: string | null;
  /** Why sessions were ended, e.g. `password_changed`. */
  reason: string | null;
  client: string;
  deviceClass: string;
  /** The source network prefix, e.g. `203.0.113.0/24`. Never a full address. */
  network: string | null;
  initiatedByAdmin: boolean;
  /** Quoted to support to find the related records. */
  reference: string | null;
}

export interface SecurityEventsPage {
  items: AccountSecurityEvent[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

/** The largest page the API serves. */
export const SECURITY_EVENTS_MAX_PAGE_SIZE = 50;

/** The furthest page the API accepts (`AuthSecurityHistoryController.MaxPage`); later pages answer 400. */
export const SECURITY_EVENTS_MAX_PAGE = 500;

function normalizeSecurityEvent(value: unknown): AccountSecurityEvent | null {
  const source = asRecord(value);
  if (!source) {
    return null;
  }

  const id = readNumber(source, 'Id', 'id');
  const kind = readString(source, 'Kind', 'kind');
  const occurredAt = readString(source, 'OccurredAt', 'occurredAt');
  if (id === undefined || !kind || !occurredAt) {
    return null;
  }

  return {
    id,
    kind,
    occurredAt,
    method: readNullableString(source, 'Method', 'method') ?? null,
    factor: readNullableString(source, 'Factor', 'factor') ?? null,
    reason: readNullableString(source, 'Reason', 'reason') ?? null,
    client: readString(source, 'Client', 'client') ?? 'unknown',
    deviceClass: readString(source, 'DeviceClass', 'deviceClass') ?? 'unknown',
    network: readNullableString(source, 'Network', 'network') ?? null,
    initiatedByAdmin: readBoolean(source, 'InitiatedByAdmin', 'initiatedByAdmin') ?? false,
    reference: readNullableString(source, 'Reference', 'reference') ?? null,
  };
}

function readItems(source: UnknownRecord): unknown[] {
  const items = source['Items'] ?? source['items'];
  return Array.isArray(items) ? items : [];
}

/**
 * Accepts either casing and drops entries missing the fields a row needs, so one malformed entry
 * cannot blank the whole list.
 */
export function normalizeSecurityEventsPage(value: unknown): SecurityEventsPage | null {
  const source = asRecord(value);
  if (!source) {
    return null;
  }

  const items = readItems(source)
    .map(normalizeSecurityEvent)
    .filter((entry): entry is AccountSecurityEvent => entry !== null);
  const pageSize = readNumber(source, 'PageSize', 'pageSize') ?? items.length;
  const totalCount = readNumber(source, 'TotalCount', 'totalCount') ?? items.length;

  return {
    items,
    totalCount,
    page: readNumber(source, 'Page', 'page') ?? 1,
    pageSize,
    totalPages:
      readNumber(source, 'TotalPages', 'totalPages') ??
      (pageSize > 0 ? Math.ceil(totalCount / pageSize) : 0),
  };
}

@Injectable({ providedIn: 'root' })
export class SecurityHistoryService {
  private readonly url = `${environment.backendUrl}/auth/security-events`;

  constructor(
    private api: ApiClient,
    private authToken: AuthTokenService,
  ) {}

  /** One page of the caller's own security history, newest first. */
  getSecurityEvents(page = 1, pageSize = 20): Observable<SecurityEventsPage> {
    const params = new HttpParams()
      .set('page', Math.min(SECURITY_EVENTS_MAX_PAGE, Math.max(1, Math.trunc(page))))
      .set('pageSize', Math.min(SECURITY_EVENTS_MAX_PAGE_SIZE, Math.max(1, Math.trunc(pageSize))));

    return from(this.authToken.ensureCsrfToken()).pipe(
      switchMap(() =>
        this.api.get<ApiEnvelope<unknown>>(this.url, { params, withCredentials: true }),
      ),
      map((res) => {
        const page = normalizeSecurityEventsPage(
          requireEnvelopeData(res, 'Security history response was incomplete.'),
        );
        if (!page) {
          throw new Error('Security history response was incomplete.');
        }
        return page;
      }),
    );
  }
}
