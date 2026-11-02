(function () {
    'use strict';
    function info(order) {
        const customerId = Number(order?.customerId ?? order?.customer?.id);
        const summary = order?.rewardSummary;
        const wholesale = String(order?.customerPriceTier || order?.customer?.priceTier || '').trim().toUpperCase() === 'WHOLESALE';
        const sameCustomer = !summary?.customerId || Number(summary.customerId) === customerId;
        const value = order?.customerCurrentPoints ?? summary?.availablePoints;
        const knownPoints = (typeof value === 'number' || typeof value === 'string' && value.trim() !== '') &&
            Number.isFinite(Number(value)) && Number(value) >= 0;
        return {
            name: order?.customerName || order?.customer?.name || 'Khách lẻ',
            phone: order?.customerPhone || order?.customer?.phone || order?.customer?.phoneNumber || '',
            points: customerId > 0 && !wholesale && summary?.isPointEligible !== false && sameCustomer && knownPoints ? Number(value) : null
        };
    }
    function render(order, prefixes) {
        const customer = info(order);
        const set = (id, value) => {
            const element = document.getElementById(id);
            if (element && element.textContent !== value) element.textContent = value;
            return element;
        };
        for (const prefix of prefixes) {
            set(prefix + 'Name', customer.name);
            const phone = set(prefix + 'Phone', customer.phone);
            if (phone) phone.hidden = !customer.phone;
            set(prefix + 'Points', customer.points == null ? '' : customer.points.toLocaleString('vi-VN', { maximumFractionDigits: 2 }));
            const points = document.getElementById(prefix + 'PointsBox');
            if (points) points.hidden = customer.points == null;
        }
        return customer;
    }
    window.CustomerDisplayCustomer = { info, render };
})();
