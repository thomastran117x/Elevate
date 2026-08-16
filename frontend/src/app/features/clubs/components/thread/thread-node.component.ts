import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AvatarComponent } from '../../../../shared/common/avatar/avatar.component';
import { User } from '../../../../core/stores/user.model';
import { ThreadLabels, ThreadReaction } from './thread-data-source';
import {
  ThreadDisplayNode,
  ThreadNodeAction,
  formatThreadTime,
  formatThreadTimeExact,
  isGroupedWithPrevious,
  threadAuthorName,
} from './thread-item';

/**
 * One message in a thread, rendering its own children recursively.
 *
 * Emits every mutation upward; the thread component owns all state and service calls.
 */
@Component({
  selector: 'app-thread-node',
  standalone: true,
  imports: [FormsModule, AvatarComponent],
  templateUrl: './thread-node.component.html',
})
export class ThreadNodeComponent {
  @Input({ required: true }) node!: ThreadDisplayNode;
  @Input({ required: true }) labels!: ThreadLabels;
  @Input() currentUser: User | null = null;
  @Input() depth = 0;
  /** Continues the previous author's run: avatar and header are suppressed. */
  @Input() grouped = false;
  @Output() action = new EventEmitter<ThreadNodeAction>();

  /** Beyond this depth nesting stops indenting, or deep threads run out of width. */
  static readonly MaxIndentDepth = 4;

  /** Collapsed subtrees keep their loaded children in memory. */
  collapsed = false;

  get indentPx(): number {
    return this.depth < ThreadNodeComponent.MaxIndentDepth ? 12 : 0;
  }

  get isOwn(): boolean {
    return !!this.currentUser && this.node.userId === this.currentUser.Id;
  }

  get authorName(): string {
    return threadAuthorName(this.node, this.labels.deletedText);
  }

  get isEdited(): boolean {
    return !this.node.isDeleted && this.node.updatedAt !== this.node.createdAt;
  }

  get childCountLabel(): string {
    const count = this.node.directReplyCount;
    return `${count} ${count === 1 ? this.labels.singular : this.labels.plural}`;
  }

  formatDate(value: string): string {
    return formatThreadTime(value);
  }

  exactDate(value: string): string {
    return formatThreadTimeExact(value);
  }

  isChildGrouped(index: number): boolean {
    return isGroupedWithPrevious(this.node.children, index);
  }

  toggleReply(): void {
    if (this.node.replyOpen) {
      this.cancelReply();
      return;
    }
    this.node.replyOpen = true;
    this.node.error = '';
  }

  /**
   * Closes the nested composer and discards its draft. Without clearing the draft and
   * reporting it, the typing heartbeat would keep running for a composer nobody can see.
   */
  cancelReply(): void {
    this.node.replyOpen = false;
    this.node.replyText = '';
    this.node.error = '';
    this.action.emit({ type: 'typing', node: this.node, active: false });
  }

  submitChild(): void {
    // Guarded on `busy` as well as content: only the button is disabled while a post is in
    // flight, so Enter could otherwise fire a second create and persist a duplicate.
    if (this.node.busy) return;
    const content = this.node.replyText.trim();
    if (content) this.action.emit({ type: 'create', node: this.node, content });
  }

  /** Nested composers feed the same thread-wide typing indicator as the root one. */
  onReplyInput(): void {
    this.action.emit({
      type: 'typing',
      node: this.node,
      active: this.node.replyText.trim().length > 0,
    });
  }

  startEdit(): void {
    this.node.editOpen = true;
    this.node.editText = this.node.content ?? '';
    this.node.error = '';
  }

  saveEdit(): void {
    if (this.node.busy) return;
    const content = this.node.editText.trim();
    if (content) this.action.emit({ type: 'edit', node: this.node, content });
  }

  react(reaction: ThreadReaction): void {
    this.action.emit({ type: 'react', node: this.node, reaction });
  }

  /** Enter sends, Shift+Enter inserts a newline. */
  onComposerKeydown(event: KeyboardEvent, submit: () => void): void {
    if (event.key !== 'Enter' || event.shiftKey) return;
    event.preventDefault();
    submit();
  }
}
