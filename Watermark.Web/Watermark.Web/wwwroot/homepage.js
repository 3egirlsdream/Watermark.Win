/* ============================================
   homepage.js - 滚动动画引擎
   ============================================ */

window.HomepageAnimations = {
    /**
     * 初始化滚动动画引擎
     * 由 Blazor OnAfterRenderAsync 调用
     */
    init: function () {
        var elements = document.querySelectorAll('.scroll-animate');

        // IntersectionObserver 不支持时的降级处理
        if (!('IntersectionObserver' in window)) {
            elements.forEach(function (el) {
                el.classList.add('animated');
            });
            return;
        }

        // 创建 IntersectionObserver (threshold: 0.15)
        var observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    entry.target.classList.add('animated');
                    observer.unobserve(entry.target);
                }
            });
        }, {
            threshold: 0.15
        });

        // 观察所有 .scroll-animate 元素
        elements.forEach(function (el) {
            observer.observe(el);
        });

        // 监听 scroll 事件控制 .sticky-nav 的 .visible 类切换
        var stickyNav = document.querySelector('.sticky-nav');
        if (stickyNav) {
            var onScroll = function () {
                if (window.scrollY > window.innerHeight) {
                    stickyNav.classList.add('visible');
                } else {
                    stickyNav.classList.remove('visible');
                }
            };

            window.addEventListener('scroll', onScroll, { passive: true });

            // 初始检查
            onScroll();
        }
    }
};

(function () {
    var apiBase = window.__LITOGRAPH_RELEASE_API__
        || (["127.0.0.1", "localhost"].includes(window.location.hostname)
            ? "http://127.0.0.1:4396"
            : "http://thankful.top:4396");

    async function refreshReleaseLinks() {
        var links = Array.from(document.querySelectorAll("[data-release-client]"));
        if (!links.length) return;

        var clients = Array.from(new Set(links.map(function (link) {
            return link.dataset.releaseClient;
        }).filter(Boolean)));

        await Promise.all(clients.map(async function (client) {
            try {
                var response = await fetch(
                    apiBase + "/api/CloudSync/GetVersion?Client=" + encodeURIComponent(client),
                    { cache: "no-store" });
                if (!response.ok) return;

                var payload = await response.json();
                var path = payload && payload.success && payload.data && payload.data.PATH;
                if (!path) return;

                var downloadUrl = new URL(path, window.location.href);
                if (downloadUrl.protocol !== "http:" && downloadUrl.protocol !== "https:") return;

                links.filter(function (link) {
                    return link.dataset.releaseClient === client;
                }).forEach(function (link) {
                    link.href = downloadUrl.href;
                });
            } catch (error) {
                console.debug("Latest release lookup failed", client, error);
            }
        }));
    }

    function scheduleRefresh() {
        void refreshReleaseLinks();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", scheduleRefresh, { once: true });
    } else {
        scheduleRefresh();
    }

    if (window.Blazor && typeof window.Blazor.addEventListener === "function") {
        window.Blazor.addEventListener("enhancedload", scheduleRefresh);
    }
}());
