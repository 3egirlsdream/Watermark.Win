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
