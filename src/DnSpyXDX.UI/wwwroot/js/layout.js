window.dnSpyXdx = window.dnSpyXdx || {};
window.dnSpyXdx.applyTheme = function (theme) {
  document.documentElement.dataset.theme = theme || "default";
};
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
window.dnSpyXdx.initBlockStructure = function (source) {
  if (!source || source._blockStructureObserver) return;
  const schedule = () => {
    if (source._blockStructureFrame) cancelAnimationFrame(source._blockStructureFrame);
    source._blockStructureFrame = requestAnimationFrame(() => {
      source._blockStructureFrame = 0;
      window.dnSpyXdx.renderBlockStructure(source);
    });
  };
  source._blockStructureObserver = new ResizeObserver(schedule);
  source._blockStructureObserver.observe(source);
  if (document.fonts?.ready) document.fonts.ready.then(schedule);
  schedule();
};
window.dnSpyXdx.renderBlockStructure = function (source) {
  if (!source) return;
  source.querySelector(":scope > .block-structure-layer")?.remove();
  const code = source.querySelector(":scope > code");
  if (!code) return;

  const pairs = new Map();
  code.querySelectorAll(".code-brace[data-brace-pair]").forEach(brace => {
    const pair = brace.dataset.bracePair;
    if (!pairs.has(pair)) pairs.set(pair, []);
    pairs.get(pair).push(brace);
  });

  const guides = [];
  const sourceBounds = source.getBoundingClientRect();
  pairs.forEach(braces => {
    if (braces.length !== 2) return;
    const opening = braces[0].getBoundingClientRect();
    const closing = braces[1].getBoundingClientRect();
    // dnSpy omits the brace lines and places the guide at whichever brace is less indented.
    const top = opening.bottom - sourceBounds.top + source.scrollTop;
    const bottom = closing.top - sourceBounds.top + source.scrollTop;
    if (bottom - top < 1) return;
    const openingX = opening.left + opening.width / 2;
    const closingX = closing.left + closing.width / 2;
    guides.push({
      x: Math.round(Math.min(openingX, closingX) - sourceBounds.left + source.scrollLeft) + 0.5,
      top: Math.round(top) + 0.5,
      bottom: Math.round(bottom) + 0.5,
      color: getComputedStyle(braces[0]).color
    });
  });
  if (guides.length === 0) return;

  // Keep the fixed SVG namespace from looking like a fetchable asset to offline-asset scans.
  const svgNamespace = "http" + "://www.w3.org/2000/svg";
  const layer = document.createElementNS(svgNamespace, "svg");
  layer.classList.add("block-structure-layer");
  layer.setAttribute("aria-hidden", "true");
  layer.setAttribute("width", Math.max(source.scrollWidth, source.clientWidth));
  layer.setAttribute("height", Math.max(source.scrollHeight, source.clientHeight));
  guides.forEach(guide => {
    const line = document.createElementNS(svgNamespace, "line");
    line.classList.add("block-structure-guide");
    line.setAttribute("x1", guide.x);
    line.setAttribute("x2", guide.x);
    line.setAttribute("y1", guide.top);
    line.setAttribute("y2", guide.bottom);
    line.style.stroke = guide.color;
    layer.appendChild(line);
  });
  source.insertBefore(layer, code);
};
window.dnSpyXdx.disposeBlockStructure = function (source) {
  if (!source) return;
  if (source._blockStructureFrame) cancelAnimationFrame(source._blockStructureFrame);
  source._blockStructureFrame = 0;
  source._blockStructureObserver?.disconnect();
  source._blockStructureObserver = null;
  source.querySelector(":scope > .block-structure-layer")?.remove();
};
window.dnSpyXdx.scrollTreeNodeIntoView = function (row) {
  if (row) row.scrollIntoView({ block: "nearest", inline: "nearest", behavior: "auto" });
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
  window.dnSpyXdx.clearSourceFind(source);
};
window.dnSpyXdx.clearSourceFind = function (source) {
  if (!CSS.highlights) return;
  CSS.highlights.clear();
  // Some embedded Chromium builds defer repainting custom highlights until the
  // highlighted element is invalidated.
  if (source) source.style.setProperty("--source-find-repaint", Date.now());
};
window.dnSpyXdx.findInSource = function (source, query, requestedIndex) {
  window.dnSpyXdx.clearSourceFind(source);
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
