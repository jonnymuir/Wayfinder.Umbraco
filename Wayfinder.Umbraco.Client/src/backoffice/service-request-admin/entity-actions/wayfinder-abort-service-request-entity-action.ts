import { html } from 'lit';
import { createRef, ref } from 'lit/directives/ref.js';
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { umbConfirmModal } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import { UmbRequestReloadStructureForEntityEvent } from '@umbraco-cms/backoffice/entity-action';
import { serviceRequestAdminFetch } from '../service-request-admin-http.js';

/**
 * Soft-terminates a stuck instance — never a generic `kind: 'delete'` action (this abandons a
 * still-in-progress instance, so it needs its own confirmation wording, danger colour, and a
 * required reason for the audit trail; a plain "are you sure you want to delete this" would be
 * both wrong and, per Wayfinder.Engine.AbortInstance's own contract, wouldn't actually delete
 * anything anyway). The reason field lives inline inside the confirm modal's own content — the
 * built-in confirm modal has no dedicated input slot, so the <uui-textarea> is rendered as part
 * of `content` and read back via a ref once the promise resolves (rejects on cancel).
 */
export class UmbAbortServiceRequestEntityAction extends UmbEntityActionBase<never> {
  async execute() {
    if (!this.args.unique) {
      throw new Error('Cannot abort an instance without a unique identifier.');
    }

    const reasonRef = createRef<HTMLTextAreaElement>();

    await umbConfirmModal(this, {
      headline: 'Stop this service request?',
      content: html`
        <p>
          This permanently stops instance <strong>${this.args.unique}</strong>. It will never be resumable by the
          citizen or advance any further — but it stays visible here for auditing, it is not deleted.
        </p>
        <uui-label for="abort-reason">Reason (visible in the audit trail)</uui-label>
        <uui-textarea id="abort-reason" ${ref(reasonRef)} rows="2"></uui-textarea>
      `,
      color: 'danger',
      confirmLabel: 'Stop this request',
    });

    const reason = reasonRef.value?.value?.trim() || 'Stopped via the backoffice admin screen.';

    const response = await serviceRequestAdminFetch(this, `/${encodeURIComponent(this.args.unique)}/abort`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ reason }),
    });

    if (!response.ok) {
      throw new Error(`Failed to abort instance (${response.status}).`);
    }

    await this.#notify();
  }

  async #notify() {
    const actionEventContext = await this.getContext(UMB_ACTION_EVENT_CONTEXT);
    if (actionEventContext) {
      actionEventContext.dispatchEvent(
        new UmbRequestReloadStructureForEntityEvent({ unique: this.args.unique, entityType: this.args.entityType }),
      );
    }

    const notificationContext = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    notificationContext?.peek('positive', { data: { message: 'Service request stopped.' } });
  }
}

export default UmbAbortServiceRequestEntityAction;
