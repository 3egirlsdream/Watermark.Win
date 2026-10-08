(() => {
    const post = async (path, body) => {
        const response = await fetch(path, {
            method: "POST", credentials: "same-origin", cache: "no-store",
            headers: { "Content-Type": "application/json" }, body: JSON.stringify(body)
        });
        if (!response.ok) {
            if (response.status === 401) throw new Error("账号或密码不正确，或登录已过期，请重新登录。");
            if (response.status === 429) throw new Error("操作过于频繁，请稍后重试。");
            throw new Error("暂时无法完成操作，请稍后重试。");
        }
        return response.status === 204 ? null : response.json();
    };
    const login = document.getElementById("website-login");
    if (login) login.addEventListener("submit", async event => {
        event.preventDefault();
        const button = login.querySelector("button");
        const status = document.getElementById("login-status");
        button.disabled = true;
        status.textContent = "正在登录…";
        try {
            await post("/account/login", {
                user: document.getElementById("account-user").value.trim(),
                password: document.getElementById("account-password").value
            });
            location.replace("/account");
        } catch (error) { status.textContent = error.message; }
        finally { document.getElementById("account-password").value = ""; button.disabled = false; }
    });
    const plans = [...document.querySelectorAll("[data-plan]")];
    const status = document.getElementById("payment-status");
    const link = document.getElementById("payment-link");
    const refresh = document.getElementById("payment-refresh");
    let order = null;
    // A refresh can resume an unpaid order, but it never supplies identity or a paid flag.
    try { order = JSON.parse(sessionStorage.getItem("litograph-order")); } catch { /* Storage may be disabled. */ }
    const showOrder = data => {
        const url = new URL(data.payUrl);
        if (url.origin !== "https://thankful.top" || url.pathname !== "/api/Watermark/OpenDesktopPay"
            || url.searchParams.get("outTradeNo") !== data.outTradeNo) throw new Error("支付链接无效，请重试。");
        order = data;
        try { sessionStorage.setItem("litograph-order", JSON.stringify(data)); } catch { /* Optional recovery only. */ }
        link.href = url.href;
        link.hidden = false;
        refresh.hidden = false;
        plans.forEach(button => button.disabled = true);
    };
    // Do not restore a stored payment URL across account changes. Status lookup verifies ownership first.
    if (order && refresh) { refresh.hidden = false; status.textContent = "可检查上一次订单的支付结果。"; }
    plans.forEach(button => button.addEventListener("click", async () => {
        if (!document.getElementById("membership-agreement").checked) {
            status.textContent = "请先阅读并同意会员服务协议。";
            return;
        }
        plans.forEach(item => item.disabled = true);
        status.textContent = "正在创建订单…";
        try {
            const data = await post("/account/membership/order", { planId: button.dataset.plan });
            showOrder(data);
            status.textContent = "订单已创建，请前往支付宝付款，完成后检查支付结果。";
        } catch (error) {
            status.textContent = error.message;
            plans.forEach(item => item.disabled = false);
        }
    }));
    if (refresh) refresh.addEventListener("click", async () => {
        if (!order) return;
        refresh.disabled = true;
        try {
            const result = await post("/account/membership/status", { outTradeNo: order.outTradeNo });
            if (result.status === "PAID") {
                try { sessionStorage.removeItem("litograph-order"); } catch { /* Optional recovery only. */ }
                status.textContent = "支付成功，会员已开通。刷新页面可查看到期时间。";
                link.hidden = true;
                refresh.hidden = true;
                // Cookie account snapshot is updated by the server after confirmed payment.
                location.reload();
            } else if (result.status === "CLOSED" || result.status === "FAILED") {
                order = null;
                try { sessionStorage.removeItem("litograph-order"); } catch { }
                link.hidden = true;
                refresh.hidden = true;
                plans.forEach(item => item.disabled = false);
                status.textContent = "订单已关闭或失败，可重新购买。";
            } else {
                status.textContent = "暂未确认付款，请完成支付宝支付后再次检查。";
                showOrder(order);
            }
        } catch (error) { status.textContent = error.message; }
        finally { refresh.disabled = false; }
    });
})();
