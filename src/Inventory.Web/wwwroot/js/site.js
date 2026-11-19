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
          card.append(code, name, qty);
          return card;
        })
      );
      total.textContent = `(${data.onHand.toLocaleString("en-IN")} on hand)`;
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
