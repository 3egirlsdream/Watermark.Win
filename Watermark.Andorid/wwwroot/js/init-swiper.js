function swiperInit(dotNetCallbackRef, callbackMethod, id, index) {
    console.log('Entered initSwiper!');
    let className = "." + id;
    window[id] = new Swiper(className, {
        observer: true,
        observeParents: true,
        observeSlideChildren: true,
        autoHeight: true,
        initialSlide: index,
        on: {
            slideChangeTransitionStart: function () {
                dotNetCallbackRef.invokeMethodAsync(callbackMethod, this.activeIndex);
                //alert(this.activeIndex);//切换结束时，告诉我现在是第几个slide
            },
        }
    });
}
