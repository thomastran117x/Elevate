import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { User } from '../../../../core/stores/user.model';
import { DiscussionReplyReaction } from '../../models/club-discussion.types';
import type {
  DiscussionReplyNode,
  ReplyNodeAction,
} from '../discussion-reply-thread/discussion-reply-thread.component';

@Component({
  selector: 'app-discussion-reply-node',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './discussion-reply-node.component.html',
})
export class DiscussionReplyNodeComponent {
  @Input({ required: true }) node!: DiscussionReplyNode;
  @Input() currentUser: User | null = null;
  @Input() depth = 0;
  @Output() action = new EventEmitter<ReplyNodeAction>();

  get isOwnReply(): boolean {
    return !!this.currentUser && this.node.userId === this.currentUser.Id;
  }

  get authorName(): string {
    if (this.node.isDeleted) return 'Deleted reply';
    return this.node.author?.name ?? this.node.author?.username ?? `User #${this.node.userId}`;
  }

  get initials(): string {
    return this.authorName
      .split(' ')
      .slice(0, 2)
      .map((part) => part[0]?.toUpperCase() ?? '')
      .join('');
  }

  formatDate(value: string): string {
    const date = new Date(value);
    const minutes = Math.floor((Date.now() - date.getTime()) / 60000);
    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes}m ago`;
    if (minutes < 1440) return `${Math.floor(minutes / 60)}h ago`;
    if (minutes < 10080) return `${Math.floor(minutes / 1440)}d ago`;
    return date.toLocaleDateString('en-CA', { month: 'short', day: 'numeric', year: 'numeric' });
  }

  toggleReply(): void {
    this.node.replyOpen = !this.node.replyOpen;
    this.node.error = '';
  }

  submitChild(): void {
    const content = this.node.replyText.trim();
    if (content) this.action.emit({ type: 'create', node: this.node, content });
  }

  startEdit(): void {
    this.node.editOpen = true;
    this.node.editText = this.node.content ?? '';
    this.node.error = '';
  }

  saveEdit(): void {
    const content = this.node.editText.trim();
    if (content) this.action.emit({ type: 'edit', node: this.node, content });
  }

  react(reaction: DiscussionReplyReaction): void {
    this.action.emit({ type: 'react', node: this.node, reaction });
  }
}
