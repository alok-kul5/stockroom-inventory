// Small progressive enhancements. Every page still works without JavaScript.

async function fetchStock(productId) {
  const res = await fetch(`/api/products/${productId}/stock`, { headers: { Accept: "application/json" } });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json();
}

// Product page: draw one card per location from the JSON API.
(function stockGrid() {
  const grid = document.getElementById("stock-grid");
  if (!grid) return;
  const total = document.getElementById("stock-total");

  fetchStock(grid.dataset.productId)
    .then((data) => {
      grid.replaceChildren(
        ...data.locations.map((l) => {
          const card = document.createElement("div");
          card.className = "loc-card" + (l.quantity === 0 ? " zero" : "");
          const code = document.createElement("div");
          code.className = "sku text-muted";
          code.textContent = l.code;
          const name = document.createElement("div");
          name.textContent = l.name;
          const qty = document.createElement("div");
          qty.className = "qty";
          qty.textContent = l.quantity.toLocaleString("en-IN");
          const bar = document.createElement("div");
          bar.className = "loc-bar";
          bar.style.setProperty("--w", data.onHand ? `${(100 * l.quantity) / data.onHand}%` : "0%");
          card.append(code, name, qty, bar);
          return card;
        })
      );
      const big = document.getElementById("stock-big");
      if (big) big.textContent = data.onHand.toLocaleString("en-IN");
      total.textContent = big ? `units on hand across ${data.locations.length} locations` : `(${data.onHand.toLocaleString("en-IN")} on hand)`;
      if (data.onHand <= data.reorderLevel) {
        total.classList.remove("text-muted");
        total.classList.add("low");
        total.textContent += " · below reorder level";
      }
    })
    .catch(() => {
      grid.textContent = "Couldn't load stock levels. Refresh to try again.";
    });
})();

// Movement form: show only the location fields the chosen type needs, and show live stock.
(function moveForm() {
  const form = document.getElementById("move-form");
  if (!form) return;
  const product = form.querySelector("#ProductId");
  const hint = document.getElementById("product-stock");
  const qtyLabel = document.getElementById("qty-label");
  const toLabel = form.querySelector("[data-label-adjustment]");

  function currentType() {
    return form.querySelector("input[name=Type]:checked")?.value ?? "Receive";
  }

  function applyType() {
    const type = currentType();
    form.querySelectorAll("[data-show-for]").forEach((el) => {
      const show = el.dataset.showFor.split(" ").includes(type);
      el.hidden = !show;
      if (!show) el.querySelector("select").value = "";
    });
    qtyLabel.textContent = type === "Adjustment" ? "Counted quantity" : "Quantity";
    toLabel.textContent = type === "Adjustment" ? "Location" : "To location";
  }

  async function showStock() {
    hint.textContent = "";
    if (!product.value) return;
    try {
      const data = await fetchStock(product.value);
      hint.textContent = data.locations
        .map((l) => `${l.code}: ${l.quantity}`)
        .join(" · ");
    } catch {
      hint.textContent = "";
    }
  }

  form.querySelectorAll("input[name=Type]").forEach((r) => r.addEventListener("change", applyType));
  product.addEventListener("change", showStock);
  applyType();
  showStock();
})();

// Details: the hero figure counts up, parcels drop onto their shelf one by one, a label
// follows the pointer over a shelf, and flash messages leave on their own.
(function details() {
  const still = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const fine = matchMedia("(hover: hover) and (pointer: fine)").matches;

  document.querySelectorAll(".parcels").forEach((row) =>
    row.querySelectorAll(".parcel").forEach((p, i) => p.style.setProperty("--n", i)));

  const fig = document.querySelector(".hero-figure");
  if (fig && !still) {
    const cur = fig.querySelector(".cur");
    const text = fig.lastChild.textContent.trim();
    const target = Number(text.replace(/,/g, ""));
    if (Number.isFinite(target)) {
      const node = fig.lastChild, t0 = performance.now();
      const step = (now) => {
        const k = Math.min(1, (now - t0) / 1300), e = 1 - (1 - k) ** 4;
        node.textContent = Math.round(target * e).toLocaleString("en-IN");
        if (k < 1) requestAnimationFrame(step); else node.textContent = text;
      };
      if (cur) requestAnimationFrame(step);
    }
  }

  if (fine && !still) {
    const label = document.createElement("div");
    label.className = "cursor-label";
    label.setAttribute("aria-hidden", "true");
    document.body.append(label);
    document.querySelectorAll(".shelf").forEach((shelf) => {
      shelf.addEventListener("pointerenter", () => { label.textContent = shelf.dataset.label; label.classList.add("on"); });
      shelf.addEventListener("pointerleave", () => label.classList.remove("on"));
      shelf.addEventListener("pointermove", (e) => {
        const over = e.target.closest(".parcel");
        label.textContent = over ? `Open ${over.querySelector(".parcel-sku").textContent}` : shelf.dataset.label;
        label.style.left = `${e.clientX}px`;
        label.style.top = `${e.clientY}px`;
      });
    });
  }

  document.querySelectorAll("main > .alert-success").forEach((a) => {
    a.classList.add("flash");
    setTimeout(() => a.classList.add("gone"), 4200);
    setTimeout(() => a.remove(), 4800);
  });
})();
