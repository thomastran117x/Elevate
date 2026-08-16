import { Component } from '@angular/core';

/** Three-dot "someone is typing" animation. */
@Component({
  selector: 'app-typing-dots',
  standalone: true,
  imports: [],
  template: `<span class="inline-flex items-center gap-1" aria-hidden="true">
    <span class="dot"></span>
    <span class="dot" style="animation-delay: 150ms"></span>
    <span class="dot" style="animation-delay: 300ms"></span>
  </span>`,
  styles: [
    `
      .dot {
        width: 0.3rem;
        height: 0.3rem;
        border-radius: 9999px;
        background-color: var(--accent);
        display: inline-block;
      }

      @media (prefers-reduced-motion: no-preference) {
        .dot {
          animation: typing-bounce 1.1s ease-in-out infinite;
        }
      }

      @keyframes typing-bounce {
        0%,
        60%,
        100% {
          transform: translateY(0);
          opacity: 0.45;
        }
        30% {
          transform: translateY(-0.2rem);
          opacity: 1;
        }
      }
    `,
  ],
})
export class TypingDotsComponent {}
