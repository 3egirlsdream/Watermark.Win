export function init(wrapper, element, inputFile) {

    //阻止浏览器默认行为
    const evt = (e) => {
        e.preventDefault();
    }
    document.addEventListener("dragleave", evt, false);
    document.addEventListener("drop", evt, false);
    document.addEventListener("dragenter", evt, false);
    document.addEventListener("dragover", evt, false);
    function drop(e) {
        try {
            var fileList = e.dataTransfer.files; //获取文件对象
            //检测是否是拖拽文件到页面的操作
            if (fileList.length == 0) {
                return false;
            }

            inputFile.files = e.dataTransfer.files;
            const event = new Event('change', { bubbles: true });
            inputFile.dispatchEvent(event);
        }
        catch (e) {
            wrapper.invokeMethodAsync('DropAlert', e);
        }
    }
    element.addEventListener("drop", drop, false);
    function paste(e) {
        inputFile.files = e.clipboardData.files;
        const event = new Event('change', { bubbles: true });
        inputFile.dispatchEvent(event);
    }
    element.addEventListener('paste', paste, false);

    return {
        dispose: () => {
            try {
                element.removeEventListener('dragleave', evt);
                element.removeEventListener("drop", drop);
                element.removeEventListener('dragenter', evt);
                element.removeEventListener('dragover', evt);
                element.removeEventListener('paste', paste);
            }
            catch (err) {
                console.log(err);
            }
        }
    }
}