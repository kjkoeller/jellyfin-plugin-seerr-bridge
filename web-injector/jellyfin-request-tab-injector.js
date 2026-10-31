(() => {
  "use strict";

  const REQUEST_URL = "/JellyseerrBridge/Page?v=0.2.3.0";
  const STYLE_ID = "jellyseerr-bridge-request-entry-style";

  // Same plus-in-a-circle icon as the side menu entry, drawn as an SVG for the
  // avatar menu because the Modern layout may not load the icon font.
  const REQUEST_ICON_SVG = '<svg viewBox="0 0 24 24" width="24" height="24" aria-hidden="true" focusable="false" fill="currentColor"><path d="M13 7h-2v4H7v2h4v4h2v-4h4v-2h-4V7zm-1-5C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z"/></svg>';

  function textOf(node) {
    return [
      node?.textContent,
      node?.getAttribute?.("aria-label"),
      node?.getAttribute?.("title"),
      node?.getAttribute?.("data-title")
    ].filter(Boolean).join(" ").replace(/\s+/g, " ").trim().toLowerCase();
  }

  function ensureStyle() {
    if (document.getElementById(STYLE_ID)) return;
    const style = document.createElement("style");
    style.id = STYLE_ID;
    style.textContent = ".jellyseerr-bridge-request-menu-item{align-items:center;color:inherit;display:flex!important;gap:1rem;min-height:3rem;padding:.75rem 1.25rem;text-decoration:none}.jellyseerr-bridge-request-menu-item:hover,.jellyseerr-bridge-request-menu-item:focus,.jellyseerr-bridge-request-tab:hover,.jellyseerr-bridge-request-tab:focus{color:#00a4dc;text-decoration:none}.jellyseerr-bridge-request-menu-icon{align-items:center;display:inline-flex;font-size:1.45rem;justify-content:center;line-height:1;min-width:1.5rem}.jellyseerr-bridge-request-menu-label{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.jellyseerr-bridge-request-tab{align-items:center;color:inherit;display:inline-flex;text-decoration:none}.jellyseerr-bridge-user-menu-item{align-items:center;color:inherit;display:flex;gap:1rem;text-decoration:none}.jellyseerr-bridge-user-menu-item svg{opacity:.7}";
    document.head.appendChild(style);
  }

  function controlsIn(container) {
    return Array.from(container.querySelectorAll("a, button, [role='button'], [role='menuitem'], [role='link'], [role='tab']"));
  }

  function openRequestPage(event) {
    event.preventDefault();
    event.stopPropagation();
    event.stopImmediatePropagation?.();
    window.location.assign(REQUEST_URL);
  }

  function wireLink(link) {
    link.href = REQUEST_URL;
    link.addEventListener("click", openRequestPage, true);
    return link;
  }

  function sideMenuContainers() {
    const selectors = [
      ".mainDrawer-scrollContainer",
      ".mainDrawer .scrollContainer",
      ".drawerContent",
      ".navMenu",
      ".MuiDrawer-paper"
    ];
    const found = new Set();
    for (const selector of selectors) {
      for (const candidate of document.querySelectorAll(selector)) {
        if (candidate.classList?.contains("mainDrawerHandle")) continue;
        const labels = controlsIn(candidate).map(textOf);
        const hasNormalNav = labels.includes("home")
          || labels.includes("favorites")
          || labels.includes("favourites")
          || labels.some((label) => label.includes("home"));
        if (hasNormalNav) found.add(candidate);
      }
    }
    return Array.from(found);
  }

  function isDrawerElement(node) {
    return Boolean(node?.closest?.(".mainDrawer,.mainDrawer-scrollContainer,.drawerContent,.MuiDrawer-paper"));
  }

  function labelMatches(node, labels) {
    const label = textOf(node);
    return labels.some((value) => label === value || label.includes(value));
  }

  function topNavContainers() {
    const found = new Set();
    const selectors = [
      ".sectionTabs",
      ".emby-tabs",
      ".tabs",
      ".headerTabs",
      ".viewMenuBar",
      ".skinHeader .tabs",
      // The Modern layout's Home/Favorites tabs are an MUI tab list.
      ".MuiTabs-flexContainer",
      "[role='tablist']"
    ];

    for (const selector of selectors) {
      for (const candidate of document.querySelectorAll(selector)) {
        if (isDrawerElement(candidate)) continue;
        const controls = controlsIn(candidate);
        const hasHome = controls.some((item) => labelMatches(item, ["home"]));
        const hasFavorites = controls.some((item) => labelMatches(item, ["favorites", "favourites"]));
        if (hasHome && hasFavorites) found.add(candidate);
      }
    }

    for (const home of controlsIn(document).filter((item) => !isDrawerElement(item) && labelMatches(item, ["home"]))) {
      let candidate = home.parentElement;
      for (let depth = 0; candidate && depth < 4; depth += 1, candidate = candidate.parentElement) {
        const controls = controlsIn(candidate);
        const hasFavorites = controls.some((item) => labelMatches(item, ["favorites", "favourites"]));
        if (hasFavorites && controls.length <= 12) {
          found.add(candidate);
          break;
        }
      }
    }

    return Array.from(found).sort((left, right) => controlsIn(left).length - controlsIn(right).length);
  }

  function createMenuItem(reference) {
    const link = document.createElement("a");
    link.className = "jellyseerr-bridge-request-menu-item";
    link.setAttribute("aria-label", "Requests");
    wireLink(link);

    const icon = document.createElement("span");
    icon.className = "material-icons jellyseerr-bridge-request-menu-icon";
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "add_circle_outline";

    const label = document.createElement("span");
    label.className = "jellyseerr-bridge-request-menu-label";
    label.textContent = "Requests";

    link.append(icon, label);

    const referenceClass = reference?.getAttribute("class");
    if (referenceClass) link.setAttribute("class", `${referenceClass} jellyseerr-bridge-request-menu-item`);
    const referenceRole = reference?.getAttribute("role");
    if (referenceRole) link.setAttribute("role", referenceRole);

    return link;
  }

  function createTopTab(reference) {
    const tag = reference?.tagName?.toLowerCase() === "button" ? "button" : "a";
    const link = document.createElement(tag);
    link.className = "jellyseerr-bridge-request-tab";
    link.textContent = "Request";
    link.setAttribute("aria-label", "Request");
    link.setAttribute("data-jellyseerr-bridge", "request-tab");
    if (tag === "button") link.type = "button";
    wireLink(link);

    const referenceClass = reference?.getAttribute("class");
    if (referenceClass) link.setAttribute("class", `${referenceClass} jellyseerr-bridge-request-tab`);
    const referenceRole = reference?.getAttribute("role");
    if (referenceRole) link.setAttribute("role", referenceRole);
    const referenceTabIndex = reference?.getAttribute("tabindex");
    if (referenceTabIndex) link.setAttribute("tabindex", referenceTabIndex);
    link.classList.remove("selected", "active", "is-active", "current", "Mui-selected");
    // MUI tabs mark the current tab with aria-selected, so ours has to say it isn't.
    if (referenceRole === "tab") {
      link.setAttribute("aria-selected", "false");
      link.setAttribute("tabindex", "-1");
    } else {
      link.removeAttribute("aria-selected");
    }

    return link;
  }

  function insertTopTab() {
    ensureStyle();
    const existingTabs = Array.from(document.querySelectorAll(".jellyseerr-bridge-request-tab"));
    existingTabs.slice(1).forEach((node) => node.remove());
    if (existingTabs.length > 0) return;

    for (const container of topNavContainers()) {
      const controls = controlsIn(container);
      const after = controls.find((item) => /^(favorites|favourites)$/i.test(textOf(item)))
        || controls.find((item) => /^home$/i.test(textOf(item)) || textOf(item).includes("home"));
      if (!after) continue;
      const link = createTopTab(after);
      after.insertAdjacentElement("afterend", link);
      return;
    }
  }

  function insertSideMenuItem() {
    ensureStyle();

    for (const container of sideMenuContainers()) {
      if (container.querySelector(".jellyseerr-bridge-request-menu-item")) continue;
      const controls = controlsIn(container);
      const after = controls.find((item) => /^(favorites|favourites)$/i.test(textOf(item)))
        || controls.find((item) => /^home$/i.test(textOf(item)) || textOf(item).includes("home"))
        || controls[0];
      if (!after) continue;
      const link = createMenuItem(after);
      if (after.parentElement === container) after.insertAdjacentElement("afterend", link);
      else after.parentElement?.insertAdjacentElement("afterend", link);
    }
  }

  // The Modern layout (default in Jellyfin 12) has no side drawer on desktop,
  // and the Home/Favorites tabs only exist on the home page. The avatar menu
  // entry means Requests can still be reached from every other page.

  function userMenuLists() {
    const lists = document.querySelectorAll(".MuiPopover-root ul[role='menu'], .MuiMenu-root ul[role='menu']");
    return Array.from(lists).filter((list) => {
      const labels = Array.from(list.querySelectorAll("[role='menuitem']")).map(textOf);
      return labels.some((label) => label.includes("sign out") || label.includes("log out") || label.includes("logout"));
    });
  }

  function insertUserMenuItem() {
    for (const list of userMenuLists()) {
      if (list.querySelector(".jellyseerr-bridge-user-menu-item")) continue;

      const items = Array.from(list.querySelectorAll("[role='menuitem']"));
      const signOut = items.find((item) => /sign out|log ?out/.test(textOf(item)));
      const reference = items.find((item) => item !== signOut) || items[0];

      ensureStyle();
      const link = document.createElement("a");
      link.setAttribute("role", "menuitem");
      link.setAttribute("tabindex", "-1");
      // Borrow the MUI classes from a real menu item so spacing and hover match.
      link.className = `${reference?.getAttribute("class") || ""} jellyseerr-bridge-user-menu-item`.trim();
      link.innerHTML = `${REQUEST_ICON_SVG}<span>Requests</span>`;
      wireLink(link);

      if (signOut && signOut.parentElement === list) list.insertBefore(link, signOut);
      else list.appendChild(link);
    }
  }

  function insertEntries() {
    insertTopTab();
    insertSideMenuItem();
    insertUserMenuItem();
  }

  insertEntries();
  setInterval(insertEntries, 1000);
  new MutationObserver(insertEntries).observe(document.documentElement, { childList: true, subtree: true });
})();
