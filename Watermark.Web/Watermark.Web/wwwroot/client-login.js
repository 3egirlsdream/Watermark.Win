(async () => {
    const ticket = new URLSearchParams(location.hash.slice(1)).get("ticket");
    history.replaceState(null, "", location.pathname);
    try {
        if (!ticket) throw new Error();
        const response = await fetch("/account/client-session/redeem", {
            method: "POST", credentials: "same-origin", cache: "no-store",
            headers: { "Content-Type": "application/json" }, body: JSON.stringify({ ticket })
        });
        if (!response.ok) throw new Error();
        location.replace("/account");
    } catch {
        document.getElementById("status").textContent = "登录链接已失效，请从轻影客户端重新打开官网。";
    }
})();
