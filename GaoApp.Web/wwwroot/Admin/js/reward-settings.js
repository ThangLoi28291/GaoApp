(function () {
    'use strict';

    const page =
        document.querySelector(
            '[data-reward-settings-page]'
        );

    if (!page) {
        return;
    }

    document.getElementById('rewardCategorySearch')?.addEventListener('input', function () {
        const normalize = value => value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/[đĐ]/g, 'd').toLowerCase();
        const term = normalize(this.value.trim());
        document.querySelectorAll('[data-reward-category]').forEach(option => {
            option.hidden = !normalize(option.dataset.rewardCategory || '').includes(term);
        });
    });

    const form =
        document.getElementById(
            'rewardSettingsForm'
        );

    const moneyInput =
        document.getElementById(
            'moneyPerPoint'
        );

    const pointsInput =
        document.getElementById(
            'pointsPerVoucher'
        );

    const voucherInput =
        document.getElementById(
            'voucherValue'
        );

    const confirmInput =
        document.getElementById(
            'confirmRateChange'
        );

    const confirmModalEl =
        document.getElementById(
            'rewardSettingsConfirmModal'
        );

    const confirmButton =
        document.getElementById(
            'btnConfirmRewardRateChange'
        );

    const original = {
        moneyPerPoint:
            Number(
                page.dataset
                    .originalMoneyPerPoint ||
                0
            ),

        pointsPerVoucher:
            Number(
                page.dataset
                    .originalPointsPerVoucher ||
                0
            ),

        voucherValue:
            Number(
                page.dataset
                    .originalVoucherValue ||
                0
            )
    };

    let allowConfirmedSubmit =
        false;

    function numberValue(input) {
        const value =
            Number(input?.value || 0);

        return Number.isFinite(value)
            ? value
            : 0;
    }

    function formatMoney(value) {
        const safe =
            Number.isFinite(value)
                ? value
                : 0;

        return safe.toLocaleString(
            'vi-VN',
            {
                maximumFractionDigits: 2
            }
        );
    }

    function getCurrentPolicy() {
        return {
            moneyPerPoint:
                numberValue(
                    moneyInput
                ),

            pointsPerVoucher:
                numberValue(
                    pointsInput
                ),

            voucherValue:
                numberValue(
                    voucherInput
                )
        };
    }

    function buildSummary(policy) {
        return [
            `${formatMoney(policy.moneyPerPoint)}đ / điểm`,
            `${formatMoney(policy.pointsPerVoucher)} điểm / voucher`,
            `voucher ${formatMoney(policy.voucherValue)}đ`
        ].join(' · ');
    }

    function hasRateChanged() {
        const current =
            getCurrentPolicy();

        return (
            current.moneyPerPoint !==
            original.moneyPerPoint
            ||
            current.pointsPerVoucher !==
            original.pointsPerVoucher
            ||
            current.voucherValue !==
            original.voucherValue
        );
    }

    function renderPreview() {
        const policy =
            getCurrentPolicy();

        const required =
            policy.moneyPerPoint > 0
                && policy.pointsPerVoucher > 0
                ? policy.moneyPerPoint
                * policy.pointsPerVoucher
                : 0;

        const rate =
            required > 0
                ? (
                    policy.voucherValue
                    / required
                    * 100
                )
                : 0;

        document.getElementById(
            'previewMoneyPerPoint'
        ).textContent =
            `${formatMoney(
                policy.moneyPerPoint
            )}đ`;

        document.getElementById(
            'previewPointsPerVoucher'
        ).textContent =
            `${formatMoney(
                policy.pointsPerVoucher
            )} điểm`;

        document.getElementById(
            'previewRequiredAmount'
        ).textContent =
            `${formatMoney(
                required
            )}đ`;

        document.getElementById(
            'previewVoucherValue'
        ).textContent =
            `${formatMoney(
                policy.voucherValue
            )}đ`;

        document.getElementById(
            'previewRewardRate'
        ).textContent =
            `${rate.toLocaleString(
                'vi-VN',
                {
                    maximumFractionDigits: 2
                }
            )}%`;
    }

    function showRateChangeConfirmation() {
        const current =
            getCurrentPolicy();

        document.getElementById(
            'oldPolicySummary'
        ).textContent =
            buildSummary(
                original
            );

        document.getElementById(
            'newPolicySummary'
        ).textContent =
            buildSummary(
                current
            );

        if (
            !confirmModalEl ||
            !window.bootstrap
        ) {
            return false;
        }

        bootstrap.Modal
            .getOrCreateInstance(
                confirmModalEl
            )
            .show();

        return true;
    }

    [
        moneyInput,
        pointsInput,
        voucherInput
    ].forEach(function (input) {
        input?.addEventListener(
            'input',
            renderPreview
        );
    });

    form?.addEventListener(
        'submit',
        function (event) {

            if (
                allowConfirmedSubmit
                || page.dataset.pageState !==
                'existing'
                || !hasRateChanged()
            ) {
                if (confirmInput) {
                    confirmInput.value =
                        allowConfirmedSubmit
                            ? 'true'
                            : 'false';
                }

                return;
            }

            event.preventDefault();

            showRateChangeConfirmation();
        }
    );

    confirmButton
        ?.addEventListener(
            'click',
            function () {

                allowConfirmedSubmit =
                    true;

                if (confirmInput) {
                    confirmInput.value =
                        'true';
                }

                if (
                    confirmModalEl &&
                    window.bootstrap
                ) {
                    bootstrap.Modal
                        .getInstance(
                            confirmModalEl
                        )
                        ?.hide();
                }

                if (
                    typeof form?.requestSubmit ===
                    'function'
                ) {
                    form.requestSubmit();
                } else {
                    form?.submit();
                }
            }
        );

    renderPreview();
})();
