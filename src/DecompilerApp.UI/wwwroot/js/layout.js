window.dnSpyXdx = window.dnSpyXdx || {};
window.dnSpyXdx.initExplorerResize = function (explorer, dotNet) {
  if (!explorer || explorer.dataset.resizeReady) return;
  explorer.dataset.resizeReady = "true";
  const handle = explorer.querySelector(".explorer-resizer");
  handle.addEventListener("pointerdown", event => {
    event.preventDefault();
    handle.setPointerCapture(event.pointerId);
    document.body.classList.add("resizing-explorer");
    const startX = event.clientX;
    const startWidth = explorer.getBoundingClientRect().width;
    let latestX = startX;
    let animationFrame = 0;
    const applyWidth = () => {
      animationFrame = 0;
      const maximum = window.innerWidth * 0.65;
      const width = Math.max(190, Math.min(maximum, startWidth + latestX - startX));
      explorer.style.width = width + "px";
    };
    const move = moveEvent => {
      latestX = moveEvent.clientX;
      if (!animationFrame) animationFrame = requestAnimationFrame(applyWidth);
    };
    const stop = () => {
      if (animationFrame) {
        cancelAnimationFrame(animationFrame);
        applyWidth();
      }
      document.body.classList.remove("resizing-explorer");
      if (dotNet) dotNet.invokeMethodAsync("ExplorerResized", explorer.getBoundingClientRect().width);
      handle.removeEventListener("pointermove", move);
      handle.removeEventListener("pointerup", stop);
      handle.removeEventListener("pointercancel", stop);
    };
    handle.addEventListener("pointermove", move);
    handle.addEventListener("pointerup", stop);
    handle.addEventListener("pointercancel", stop);
  });
};
window.dnSpyXdx.initSearchResize = function (panel, dotNet) {
  if (!panel || panel.dataset.resizeReady) return;
  panel.dataset.resizeReady = "true";
  const handle = panel.querySelector(".search-resizer");
  handle.addEventListener("pointerdown", event => {
    event.preventDefault();
    handle.setPointerCapture(event.pointerId);
    document.body.classList.add("resizing-search");
    const startY = event.clientY;
    const startHeight = panel.getBoundingClientRect().height;
    const move = moveEvent => {
      const maximum = window.innerHeight * 0.65;
      panel.style.height = Math.max(120, Math.min(maximum, startHeight + startY - moveEvent.clientY)) + "px";
    };
    const stop = () => {
      document.body.classList.remove("resizing-search");
      if (dotNet) dotNet.invokeMethodAsync("SearchPanelResized", panel.getBoundingClientRect().height);
      handle.removeEventListener("pointermove", move);
      handle.removeEventListener("pointerup", stop);
      handle.removeEventListener("pointercancel", stop);
    };
    handle.addEventListener("pointermove", move);
    handle.addEventListener("pointerup", stop);
    handle.addEventListener("pointercancel", stop);
  });
};
window.dnSpyXdx.initSourceLinks = function (source, dotNet) {
  if (!source || source.dataset.linksReady) return;
  source.dataset.linksReady = "true";
  let highlighted = [];
  let highlightedSymbol = null;
  const clearHighlight = () => {
    highlighted.forEach(node => node.classList.remove("code-link-active"));
    highlighted = [];
    highlightedSymbol = null;
  };
  const linkAt = event => {
    const link = event.target.closest(".code-link");
    return link && source.contains(link) ? link : null;
  };
  source.addEventListener("mouseover", event => {
    const link = linkAt(event);
    if (!link) { clearHighlight(); return; }
    if (link.dataset.symbol === highlightedSymbol) return;
    clearHighlight();
    // Box every occurrence of the same symbol, the way dnSpy marks references. Grouping is by
    // name rather than token so overload sets still highlight together.
    highlightedSymbol = link.dataset.symbol;
    highlighted = Array.from(source.querySelectorAll('.code-link[data-symbol="' + CSS.escape(highlightedSymbol) + '"]'));
    highlighted.forEach(node => node.classList.add("code-link-active"));
  });
  source.addEventListener("mouseleave", clearHighlight);
  source.addEventListener("click", event => {
    const link = linkAt(event);
    // dnSpy follows a reference only when no modifier or Ctrl is held; Alt and Shift are left
    // alone so they can start a text selection. Names without a token are highlight-only.
    if (!link || !link.dataset.token || event.altKey || event.shiftKey) return;
    event.preventDefault();
    clearHighlight();
    dotNet.invokeMethodAsync("NavigateToToken", Number(link.dataset.token), event.ctrlKey);
  });
};
window.dnSpyXdx.initHistoryButtons = function (dotNet) {
  if (window.dnSpyXdx.historyReady) return;
  window.dnSpyXdx.historyReady = true;
  // Mouse 4 / mouse 5. Chromium fires these as buttons 3 and 4; preventing the default on
  // mousedown stops the webview treating them as browser back/forward.
  window.addEventListener("mousedown", event => {
    if (event.button === 3 || event.button === 4) event.preventDefault();
  });
  window.addEventListener("mouseup", event => {
    if (event.button !== 3 && event.button !== 4) return;
    event.preventDefault();
    dotNet.invokeMethodAsync("NavigateHistory", event.button === 4);
  });
  window.addEventListener("keydown", event => {
    if (!event.altKey || event.ctrlKey || event.shiftKey) return;
    if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
    event.preventDefault();
    dotNet.invokeMethodAsync("NavigateHistory", event.key === "ArrowRight");
  });
};
window.dnSpyXdx.scrollSourceToTop = function (source) {
  if (source) source.scrollTop = 0;
};
window.dnSpyXdx.scrollTreeNodeIntoView = function (row) {
  if (row) row.scrollIntoView({ block: "nearest", inline: "nearest", behavior: "smooth" });
};
window.dnSpyXdx.initSourceFind = function (source, dotNet) {
  window.dnSpyXdx.sourceFindTarget = { source, dotNet };
  if (window.dnSpyXdx.sourceFindReady) return;
  window.dnSpyXdx.sourceFindReady = true;
  window.addEventListener("keydown", event => {
    if (!(event.ctrlKey || event.metaKey) || event.altKey || event.key.toLowerCase() !== "f") return;
    const target = window.dnSpyXdx.sourceFindTarget;
    if (!target) return;
    event.preventDefault();
    target.dotNet.invokeMethodAsync("OpenFind");
  });
};
window.dnSpyXdx.disposeSourceFind = function (source) {
  if (window.dnSpyXdx.sourceFindTarget?.source === source) window.dnSpyXdx.sourceFindTarget = null;
  window.dnSpyXdx.clearSourceFind();
};
window.dnSpyXdx.clearSourceFind = function () {
  if (!CSS.highlights) return;
  CSS.highlights.delete("source-find-results");
  CSS.highlights.delete("source-find-active");
};
window.dnSpyXdx.findInSource = function (source, query, requestedIndex) {
  window.dnSpyXdx.clearSourceFind();
  if (!source || !query) return { count: 0, index: -1 };
  const ranges = [];
  const needle = query.toLocaleLowerCase();
  const walker = document.createTreeWalker(source, NodeFilter.SHOW_TEXT);
  while (walker.nextNode()) {
    const node = walker.currentNode;
    const text = node.nodeValue.toLocaleLowerCase();
    let start = text.indexOf(needle);
    while (start >= 0) {
      const range = new Range();
      range.setStart(node, start);
      range.setEnd(node, start + query.length);
      ranges.push(range);
      start = text.indexOf(needle, start + Math.max(1, query.length));
    }
  }
  if (ranges.length === 0) return { count: 0, index: -1 };
  const index = ((requestedIndex % ranges.length) + ranges.length) % ranges.length;
  if (CSS.highlights) {
    CSS.highlights.set("source-find-results", new Highlight(...ranges));
    CSS.highlights.set("source-find-active", new Highlight(ranges[index]));
  }
  const match = ranges[index].startContainer.parentElement;
  if (match) match.scrollIntoView({ block: "center", inline: "nearest" });
  return { count: ranges.length, index };
};
