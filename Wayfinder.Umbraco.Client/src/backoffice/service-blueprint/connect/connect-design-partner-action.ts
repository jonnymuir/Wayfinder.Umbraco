import { UmbCollectionActionBase } from '@umbraco-cms/backoffice/collection';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';

export class WayfinderConnectDesignPartnerAction extends UmbCollectionActionBase {
  override async execute(): Promise<void> {
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    if (!modalManager) {
      throw new Error('Modal manager context not found.');
    }

    const modalHandler = modalManager.open(this, 'Wayfinder.ConnectDesignPartnerModal', {
      modal: { type: 'sidebar', size: 'medium' },
    } as never);

    await modalHandler.onSubmit().catch(() => {
      // Closed: there is nothing to save.
    });
  }
}

export { WayfinderConnectDesignPartnerAction as api };
export default WayfinderConnectDesignPartnerAction;
