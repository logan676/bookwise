(() => {
  const settings = window.bookWiseSettings ?? {
    endpoints: { books: "/api/books" },
  };

  const elements = {
    searchInput: document.getElementById("search-input"),
    searchButton: document.getElementById("search-button"),
    startSearching: document.getElementById("start-searching"),
    emptyState: document.getElementById("empty-state"),
    resultsSection: document.getElementById("results-section"),
    resultsList: document.getElementById("results-list"),
    resultsTitle: document.getElementById("results-title"),
    resultsSubtitle: document.getElementById("results-subtitle"),
    noResults: document.getElementById("no-results"),
    favoriteFilter: document.getElementById("favorite-filter"),
    openAddForm: document.getElementById("open-add-form"),
    addPanel: document.getElementById("add-book-section"),
    addForm: document.getElementById("add-book-form"),
    formError: document.getElementById("form-error"),
    cancelAdd: document.getElementById("cancel-add"),
    closeAddForm: document.getElementById("close-add-form"),
  };

  const formFields = {
    title: document.getElementById("book-title"),
    author: document.getElementById("book-author"),
    category: document.getElementById("book-category"),
    description: document.getElementById("book-description"),
    coverImageUrl: document.getElementById("book-cover"),
    rating: document.getElementById("book-rating"),
    isFavorite: document.getElementById("book-favorite"),
  };

  const state = {
    query: "",
    favoritesOnly: false,
    loading: false,
    books: [],
  };

  const themeConfig = {
    storageKey: "bookwise.theme",
    defaultPreference: "forest",
    availablePreferences: [
      "light",
      "dark",
      "system",
      "forest",
      "ocean",
      "sunset",
      "rose",
      "lavender",
    ],
    systemMatcher: typeof window.matchMedia === "function"
      ? window.matchMedia("(prefers-color-scheme: dark)")
      : null,
  };

  let currentThemePreference = themeConfig.defaultPreference;

  const icons = {
    star(fill) {
      const color = fill ? "#f59e0b" : "currentColor";
      return `<svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path fill="${color}" stroke="${color}" d="M12 3.5l2.32 4.7 5.19.76-3.75 3.66.89 5.18L12 15.9l-4.65 2.45.89-5.18-3.75-3.66 5.19-.76L12 3.5z"/></svg>`;
    },
    trash() {
      return '<svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M9 3l-.5 1H5v2h14V4h-3.5L15 3H9zm1 6v8h2V9h-2zm4 0v8h2V9h-2z"/></svg>';
    },
  };

  function init() {
    if (!elements.searchInput) {
      return;
    }

    elements.searchButton?.addEventListener("click", () => triggerSearch());
    elements.startSearching?.addEventListener("click", () => {
      elements.searchInput?.focus();
      triggerSearch();
    });
    elements.searchInput?.addEventListener("keydown", (event) => {
      if (event.key === "Enter") {
        event.preventDefault();
        triggerSearch();
      }
    });
    elements.favoriteFilter?.addEventListener("change", () => {
      state.favoritesOnly = Boolean(elements.favoriteFilter?.checked);
      triggerSearch(false);
    });

    elements.openAddForm?.addEventListener("click", () => toggleAddPanel(true));

    elements.cancelAdd?.addEventListener("click", () => toggleAddPanel(false));
    elements.closeAddForm?.addEventListener("click", () =>
      toggleAddPanel(false)
    );

    elements.addForm?.addEventListener("submit", async (event) => {
      event.preventDefault();
      await saveBook();
    });
  }

  function initTheme() {
    const storedPreference = getStoredThemePreference();
    const initialPreference = storedPreference || themeConfig.defaultPreference;
    applyTheme(initialPreference);

    const buttons = document.querySelectorAll(
      ".theme-choice-group .theme-choice"
    );
    if (buttons.length) {
      buttons.forEach((button) => {
        button.addEventListener("click", () => {
          const preference = button.dataset.theme;
          if (!preference || preference === currentThemePreference) {
            applyTheme(preference || initialPreference);
            return;
          }
          applyTheme(preference, { persist: true });
        });
      });
    }

    if (!themeConfig.systemMatcher) {
      return;
    }

    const handleSystemChange = () => {
      if (currentThemePreference === "system") {
        applyTheme("system");
      }
    };

    if (typeof themeConfig.systemMatcher.addEventListener === "function") {
      themeConfig.systemMatcher.addEventListener("change", handleSystemChange);
    } else if (typeof themeConfig.systemMatcher.addListener === "function") {
      themeConfig.systemMatcher.addListener(handleSystemChange);
    }
  }

  function applyTheme(preference, options = {}) {
    if (!preference || !themeConfig.availablePreferences.includes(preference)) {
      preference = themeConfig.defaultPreference;
    }

    currentThemePreference = preference;
    const resolvedTheme =
      preference === "system"
        ? themeConfig.systemMatcher && themeConfig.systemMatcher.matches
          ? "dark"
          : "light"
        : preference;

    document.documentElement.dataset.theme = resolvedTheme;
    document.documentElement.dataset.themePreference = preference;

    updateThemeButtons(preference);

    if (options.persist) {
      storeThemePreference(preference);
    }
  }

  function updateThemeButtons(preference) {
    const buttons = document.querySelectorAll(
      ".theme-choice-group .theme-choice"
    );
    if (!buttons.length) {
      return;
    }

    buttons.forEach((button) => {
      const isActive = button.dataset.theme === preference;
      button.classList.toggle("is-active", isActive);
      button.setAttribute("aria-pressed", isActive ? "true" : "false");
    });
  }

  function getStoredThemePreference() {
    try {
      const value = localStorage.getItem(themeConfig.storageKey);
      if (value && themeConfig.availablePreferences.includes(value)) {
        return value;
      }
    } catch (error) {
      /* ignore storage errors */
    }
    return null;
  }

  function storeThemePreference(value) {
    try {
      if (!themeConfig.availablePreferences.includes(value)) {
        return;
      }
      localStorage.setItem(themeConfig.storageKey, value);
    } catch (error) {
      /* ignore storage errors */
    }
  }

  async function triggerSearch(focusEmpty = true) {
    if (!elements.searchInput) {
      return;
    }

    state.query = elements.searchInput.value.trim();
    await performSearch();

    if (focusEmpty && state.books.length === 0) {
      toggleAddPanel(true, state.query);
    }
  }

  async function performSearch() {
    setLoading(true);

    const params = new URLSearchParams();
    if (state.query) {
      params.set("search", state.query);
    }
    if (state.favoritesOnly) {
      params.set("onlyFavorites", "true");
    }

    try {
      const response = await fetch(
        `${settings.endpoints.books}?${params.toString()}`
      );
      if (!response.ok) {
        throw new Error("Unable to load books");
      }
      const data = await response.json();
      state.books = Array.isArray(data) ? data : [];
      renderBooks();
    } catch (error) {
      console.error(error);
      showErrorState("Something went wrong while loading your books.");
    } finally {
      setLoading(false);
    }
  }

  function setLoading(isLoading) {
    state.loading = isLoading;
    if (elements.searchButton) {
      elements.searchButton.disabled = isLoading;
      elements.searchButton.textContent = isLoading ? "Searching…" : "Search";
    }
  }

  function renderBooks() {
    const hasBooks = state.books.length > 0;

    elements.emptyState.hidden = hasBooks;
    elements.resultsSection.hidden = !hasBooks;
    elements.noResults.hidden = true;

    if (!hasBooks) {
      if (state.query) {
        elements.noResults.hidden = false;
        elements.noResults.textContent = `No books found for “${state.query}”. Try adding it below.`;
      }
      return;
    }

    elements.resultsTitle.textContent = state.favoritesOnly
      ? "Favorite Books"
      : "My Books";
    elements.resultsSubtitle.textContent = state.query
      ? `Showing ${state.books.length} book${
          state.books.length === 1 ? "" : "s"
        } for “${state.query}”.`
      : `You have saved ${state.books.length} book${
          state.books.length === 1 ? "" : "s"
        } in your library.`;

    elements.resultsList.innerHTML = "";
    state.books.forEach((book) => {
      const card = document.createElement("article");
      card.className = "book-card";
      card.setAttribute("role", "listitem");

      const header = document.createElement("div");
      header.className = "card-header";

      const titleBlock = document.createElement("div");
      const title = document.createElement("h3");
      title.textContent = book.title;
      const author = document.createElement("p");
      author.className = "author";
      author.textContent = book.author;
      titleBlock.append(title, author);

      const rating = document.createElement("div");
      rating.className = "meta";
      const personalRating = toNullableNumber(book.personalRating);
      const communityRating = toNullableNumber(book.publicRating);
      if (personalRating != null || communityRating != null) {
        const parts = [];
        if (personalRating != null) {
          parts.push(`My ${personalRating.toFixed(1)}`);
        }
        if (communityRating != null) {
          parts.push(`Community ${communityRating.toFixed(1)}`);
        }
        rating.innerHTML = `${icons.star(true)}<span>${parts.join(
          " · "
        )} / 5</span>`;
      } else {
        rating.textContent = "No ratings yet";
      }

      header.append(titleBlock, rating);

      const badge = document.createElement("span");
      badge.className = "badge";
      badge.textContent = book.category ?? "Uncategorized";

      const description = document.createElement("p");
      description.textContent =
        book.description ?? "No description provided yet.";

      const actions = document.createElement("div");
      actions.className = "card-actions";

      const favoriteButton = document.createElement("button");
      favoriteButton.type = "button";
      favoriteButton.className = "icon-button";
      favoriteButton.innerHTML = `${icons.star(book.isFavorite)}${
        book.isFavorite
          ? "<span>Favorited</span>"
          : "<span>Mark favorite</span>"
      }`;
      favoriteButton.addEventListener("click", () => toggleFavorite(book));

      const deleteButton = document.createElement("button");
      deleteButton.type = "button";
      deleteButton.className = "icon-button delete";
      deleteButton.innerHTML = `${icons.trash()}<span>Remove</span>`;
      deleteButton.addEventListener("click", () => deleteBook(book.id));

      actions.append(favoriteButton, deleteButton);

      card.append(header, badge, description, actions);
      elements.resultsList.appendChild(card);
    });
  }

  async function toggleFavorite(book) {
    const payload = {
      title: book.title,
      author: book.author,
      description: book.description,
      quote: book.quote,
      coverImageUrl: book.coverImageUrl,
      category: book.category,
      status: book.status,
      isFavorite: !book.isFavorite,
      personalRating: book.personalRating,
      publicRating: book.publicRating,
      isbn: book.isbn,
    };

    try {
      const response = await fetch(`${settings.endpoints.books}/${book.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      if (!response.ok) {
        throw new Error("Unable to update book");
      }
      await performSearch();
    } catch (error) {
      console.error(error);
      alert("We could not update that book. Please try again.");
    }
  }

  async function deleteBook(id) {
    const confirmDelete = confirm("Remove this book from your library?");
    if (!confirmDelete) {
      return;
    }

    try {
      const response = await fetch(`${settings.endpoints.books}/${id}`, {
        method: "DELETE",
      });
      if (!response.ok) {
        throw new Error("Unable to delete book");
      }
      await performSearch();
    } catch (error) {
      console.error(error);
      alert("We could not remove that book. Please try again.");
    }
  }

  async function saveBook() {
    if (!elements.addForm) {
      return;
    }

    const formData = new FormData(elements.addForm);
    const payload = {
      title: (formData.get("title") ?? "").toString().trim(),
      author: (formData.get("author") ?? "").toString().trim(),
      category: emptyToNull(formData.get("category")),
      description: emptyToNull(formData.get("description")),
      coverImageUrl: emptyToNull(formData.get("coverImageUrl")),
      status: 'plan-to-read',
      personalRating: toNullableNumber(formData.get("rating")),
      publicRating: null,
      isFavorite: formFields.isFavorite?.checked ?? false,
    };

    if (!payload.title || !payload.author) {
      return setFormError("Title and author are required.");
    }

    try {
      setFormError();
      elements.addForm.classList.add("is-busy");
      const response = await fetch(settings.endpoints.books, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      if (response.status === 400) {
        const problem = await response.json();
        const message = Object.values(problem.errors ?? {})
          .flat()
          .join(" ");
        throw new Error(message || "Please check the form and try again.");
      }
      if (!response.ok) {
        throw new Error("Unable to save book.");
      }

      elements.addForm.reset();
      toggleAddPanel(false);
      await performSearch();
    } catch (error) {
      console.error(error);
      setFormError(
        error instanceof Error ? error.message : "Unable to save that book."
      );
    } finally {
      elements.addForm.classList.remove("is-busy");
    }
  }

  function toggleAddPanel(show, suggestedTitle = "") {
    if (!elements.addPanel) {
      return;
    }

    elements.addPanel.hidden = !show;
    if (show) {
      formFields.title.value = suggestedTitle;
      if (suggestedTitle && !formFields.author.value) {
        formFields.author.focus();
      } else {
        formFields.title.focus();
      }
    }
  }

  function setFormError(message) {
    if (!elements.formError) {
      return;
    }

    if (!message) {
      elements.formError.hidden = true;
      elements.formError.textContent = "";
      return;
    }

    elements.formError.hidden = false;
    elements.formError.textContent = message;
  }

  function showErrorState(message) {
    elements.emptyState.hidden = false;
    elements.resultsSection.hidden = true;
    elements.noResults.hidden = false;
    elements.noResults.textContent = message;
  }

  function emptyToNull(value) {
    if (value == null) {
      return null;
    }
    const trimmed = value.toString().trim();
    return trimmed.length === 0 ? null : trimmed;
  }

  function toNullableNumber(value) {
    if (value == null || value === "") {
      return null;
    }
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  function initUserMenu() {
    const menu = document.querySelector('.settings-menu');
    if (!menu) {
      return;
    }

    const signOutButton = menu.querySelector('[data-menu-role="signout"]');
    signOutButton?.addEventListener('click', (event) => {
      event.preventDefault();
      menu.removeAttribute('open');
    });
  }

  // Navigation highlighting
  function initNavigation() {
    const navLinks = document.querySelectorAll(".nav-link");
    const currentPath = window.location.pathname;

    navLinks.forEach((link) => {
      link.classList.remove("active");

      // Check if this link matches the current page
      const linkPath =
        link.getAttribute("href") || link.getAttribute("asp-page");

      if (
        linkPath === currentPath ||
        (currentPath === "/" && (linkPath === "/Index" || linkPath === "/")) ||
        (currentPath === "/Index" && linkPath === "/Index") ||
        (currentPath === "/Explore" && linkPath === "/Explore") ||
        (currentPath === "/Statistics" && linkPath === "/Statistics") ||
        (currentPath === "/Recommendations" &&
          linkPath === "/Recommendations") ||
        (currentPath === "/Profile" && linkPath === "/Profile") ||
        (currentPath === "/Settings" && linkPath === "/Settings")
      ) {
        link.classList.add("active");
      }
    });

  }

  function initMobileNav() {
    const header = document.querySelector(".top-nav");
    const toggle = document.querySelector(".nav-toggle");
    const navigation = document.getElementById("primary-navigation");
    const overlay = document.querySelector("[data-nav-overlay]");
    const body = document.body;

    if (!header || !toggle || !navigation) {
      return;
    }

    const closeNav = () => {
      toggle.setAttribute("aria-expanded", "false");
      header.classList.remove("is-mobile-open");
      header.dataset.navOpen = "false";
      overlay?.setAttribute("hidden", "");
      body.classList.remove("nav-lock");
    };

    const openNav = () => {
      toggle.setAttribute("aria-expanded", "true");
      header.classList.add("is-mobile-open");
      header.dataset.navOpen = "true";
      overlay?.removeAttribute("hidden");
      body.classList.add("nav-lock");
    };

    toggle.addEventListener("click", () => {
      const expanded = toggle.getAttribute("aria-expanded") === "true";
      if (expanded) {
        closeNav();
      } else {
        openNav();
      }
    });

    overlay?.addEventListener("click", () => closeNav());

    navigation.querySelectorAll("a").forEach((link) => {
      link.addEventListener("click", () => closeNav());
    });

    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && header.classList.contains("is-mobile-open")) {
        closeNav();
        toggle.focus();
      }
    });

    const desktopQuery = window.matchMedia("(min-width: 901px)");
    const handleDesktopChange = (event) => {
      if (event.matches) {
        closeNav();
      }
    };

    if (typeof desktopQuery.addEventListener === "function") {
      desktopQuery.addEventListener("change", handleDesktopChange);
    } else if (typeof desktopQuery.addListener === "function") {
      desktopQuery.addListener(handleDesktopChange);
    }
  }

  document.addEventListener("DOMContentLoaded", () => {
    initTheme();
    init();
    initNavigation();
    initUserMenu();
    initMobileNav();
  });
})();
