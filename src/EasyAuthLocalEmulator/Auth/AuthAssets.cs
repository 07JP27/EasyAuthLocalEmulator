namespace EasyAuthLocalEmulator.Auth;

public static class AuthAssets
{
    public const string Styles =
        """
        :root {
          color-scheme: light;
          font-family: ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
          color: #1b1b1b;
          background: #fff;
          --border: #b8bec6;
          --border-subtle: #e1e4e8;
          --muted: #5f6670;
          --primary: #0067b8;
          --primary-hover: #005a9e;
          --danger: #a4262c;
        }
        * { box-sizing: border-box; }
        body { margin: 0; min-height: 100vh; }
        a { color: var(--primary); }
        .shell { width: min(780px, 100%); margin: 0 auto; padding: 32px 24px 0; }
        .auth-header { margin: 8px 0 24px; }
        .product-name { margin: 0; color: #343A40; font-size: .875rem; font-weight: 650; }
        .platform-name { margin: 3px 0 18px; color: var(--muted); font-size: .78rem; }
        h1 { margin: 0 0 8px; font-size: 2rem; font-weight: 550; letter-spacing: -.025em; }
        .login-platform-name { margin: 0; color: var(--muted); font-size: .875rem; }
        .auth-form { width: 100%; }
        .visually-hidden {
          position: absolute;
          width: 1px;
          height: 1px;
          padding: 0;
          margin: -1px;
          overflow: hidden;
          clip: rect(0, 0, 0, 0);
          white-space: nowrap;
          border: 0;
        }
        .form-row {
          display: grid;
          grid-template-columns: 180px minmax(0, 1fr);
          gap: 24px;
          align-items: start;
          margin: 0;
          padding: 10px 0;
          border: 0;
        }
        .form-row > label,
        .form-row > legend {
          padding-top: 9px;
          font-size: .875rem;
          font-weight: 600;
        }
        fieldset.form-row { min-width: 0; }
        fieldset.form-row > legend { float: left; width: 180px; }
        fieldset.form-row > .control { grid-column: 2; }
        .control { min-width: 0; }
        input,
        select {
          width: 100%;
          min-height: 40px;
          border: 1px solid var(--border);
          border-radius: 3px;
          padding: 8px 10px;
          color: inherit;
          background: #fff;
          font: inherit;
        }
        input[readonly] { color: #454b52; background: #f1f2f3; }
        input:focus,
        select:focus,
        button:focus-visible,
        a:focus-visible,
        summary:focus-visible {
          outline: 3px solid var(--primary);
          outline-offset: 2px;
          border-color: var(--primary);
        }
        .help {
          margin: 5px 0 0;
          color: var(--muted);
          font-size: .8rem;
          line-height: 1.4;
        }
        .inline-control { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 8px; }
        .repeat-list { display: grid; gap: 8px; }
        .repeat-row {
          display: grid;
          column-gap: 8px;
          row-gap: 5px;
          align-items: start;
        }
        .role-row {
          grid-template-columns: minmax(0, 1fr) auto;
          grid-template-areas:
            "role-input remove"
            "role-error .";
        }
        .role-row .repeat-input { grid-area: role-input; }
        .role-row .remove { grid-area: remove; }
        .role-row [data-error-for="role"] { grid-area: role-error; }
        .claim-row {
          grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) auto;
          grid-template-areas:
            "type-label value-label ."
            "type-input value-input remove"
            "type-error value-error .";
        }
        .claim-row label { margin: 0; color: var(--muted); font-size: .75rem; }
        .claim-type-label { grid-area: type-label; }
        .claim-type-input { grid-area: type-input; }
        .claim-row [data-error-for="claim-type"] { grid-area: type-error; }
        .claim-value-label { grid-area: value-label; }
        .claim-value-input { grid-area: value-input; }
        .claim-row [data-error-for="claim-value"] { grid-area: value-error; }
        .claim-row .remove { grid-area: remove; }
        .button {
          min-height: 40px;
          border: 1px solid transparent;
          border-radius: 3px;
          padding: 8px 14px;
          font: inherit;
          font-weight: 600;
          cursor: pointer;
          text-decoration: none;
          white-space: nowrap;
        }
        .primary { color: #fff; background: var(--primary); border-color: var(--primary); }
        .primary:hover { background: var(--primary-hover); border-color: var(--primary-hover); }
        .secondary { color: #32373d; background: #fff; border-color: #8a9199; }
        .secondary:hover { background: #f4f5f6; }
        .add { min-height: 32px; margin-top: 8px; padding: 4px 0; color: var(--primary); background: transparent; border: 0; font-size: .825rem; }
        .remove { min-height: 40px; color: var(--danger); background: #fff; border-color: #d5a0a3; font-size: .8rem; }
        .remove:hover { background: #fff5f5; }
        .advanced { margin-top: 18px; padding-top: 14px; border-top: 1px solid var(--border-subtle); }
        .advanced > summary {
          min-height: 40px;
          display: flex;
          align-items: center;
          gap: 9px;
          cursor: pointer;
          list-style: none;
          font-size: .875rem;
          font-weight: 600;
        }
        .advanced > summary::-webkit-details-marker { display: none; }
        .disclosure-marker {
          width: 0;
          height: 0;
          border-top: 5px solid transparent;
          border-bottom: 5px solid transparent;
          border-left: 6px solid currentColor;
          transition: transform 120ms ease-out;
          transform-origin: 3px 5px;
        }
        .advanced[open] .disclosure-marker { transform: rotate(90deg); }
        .summary-meta { margin-left: auto; color: var(--muted); font-size: .75rem; font-weight: 400; }
        .advanced-fields { padding-top: 4px; }
        .advanced-help { margin-left: 204px; }
        .form-message { margin: 18px 0 0; }
        .form-message ul { margin: 0; padding-left: 20px; }
        .form-actions {
          display: flex;
          justify-content: flex-end;
          gap: 8px;
          margin-top: 18px;
          padding-top: 18px;
          border-top: 1px solid var(--border-subtle);
        }
        .session-note { margin: 8px 0 0; color: var(--muted); font-size: .8rem; text-align: right; }
        .input-validation-error { border-color: var(--danger); }
        .validation-summary-errors, .field-validation-error { color: var(--danger); }
        .field-validation-error {
          display: block;
          margin-top: 5px;
          font-size: .8rem;
          line-height: 1.4;
        }
        .repeat-row .field-validation-error { margin-top: 0; }
        .validation-summary-valid, .field-validation-valid { display: none; }
        code { padding: 1px 3px; color: #31363d; background: #f1f2f3; border-radius: 2px; }
        .logout-content { max-width: 560px; }
        .actions { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 20px; }
        footer { margin-top: 40px; padding: 14px 0; border-top: 1px solid var(--border-subtle); color: var(--muted); font-size: .75rem; }
        @media (max-width: 640px) {
          .shell { padding: 20px 16px 0; }
          .auth-header { margin-bottom: 20px; }
          h1 { font-size: 1.65rem; }
          .form-row { grid-template-columns: 1fr; gap: 5px; padding: 9px 0; }
          .form-row > label,
          .form-row > legend { padding-top: 0; }
          fieldset.form-row > legend { float: none; width: auto; }
          fieldset.form-row > .control { grid-column: 1; }
          .claim-row {
            grid-template-columns: 1fr;
            grid-template-areas:
              "type-label"
              "type-input"
              "type-error"
              "value-label"
              "value-input"
              "value-error"
              "remove";
          }
          .repeat-row .remove { justify-self: start; }
          .advanced-help { margin-left: 0; }
          .form-actions { justify-content: flex-start; }
          .session-note { text-align: left; }
        }
        """;

    public const string LoginScript =
        """
        (() => {
          const repeaters = [];

          const reindex = (container) => {
            [...container.querySelectorAll("[data-row]")].forEach((row, index) => {
              row.querySelectorAll("[name]").forEach((input) => {
                input.name = input.name.replace(/\[\d+\]/, `[${index}]`);
              });
              row.querySelectorAll("[id]").forEach((input) => {
                input.id = input.id.replace(/_\d+__/, `_${index}__`);
              });
              row.querySelectorAll("label[for]").forEach((label) => {
                label.htmlFor = label.htmlFor.replace(/_\d+__/, `_${index}__`);
              });
              row.querySelectorAll("[aria-describedby]").forEach((element) => {
                element.setAttribute(
                  "aria-describedby",
                  element.getAttribute("aria-describedby").replace(/_\d+__/, `_${index}__`)
                );
              });
              row.querySelectorAll("[data-valmsg-for]").forEach((element) => {
                element.setAttribute(
                  "data-valmsg-for",
                  element.getAttribute("data-valmsg-for").replace(/\[\d+\]/, `[${index}]`)
                );
              });
            });
          };

          document.querySelectorAll("[data-repeat]").forEach((container) => {
            const template = document.getElementById(container.dataset.template);
            const addButton = document.querySelector(`[data-add="${container.id}"]`);
            const initialMarkup = container.innerHTML;

            const wireRemove = (row) => {
              row.querySelector("[data-remove]").addEventListener("click", () => {
                row.remove();
                reindex(container);
              });
            };

            container.querySelectorAll("[data-row]").forEach(wireRemove);
            addButton.addEventListener("click", () => {
              const index = container.querySelectorAll("[data-row]").length;
              const fragment = template.content.cloneNode(true);
              fragment.querySelectorAll(
                "[name], [id], label[for], [aria-describedby], [data-valmsg-for]"
              ).forEach((element) => {
                if (element.name) element.name = element.name.replaceAll("__index__", index);
                if (element.id) element.id = element.id.replaceAll("__index__", index);
                if (element.htmlFor) element.htmlFor = element.htmlFor.replaceAll("__index__", index);
                if (element.hasAttribute("aria-describedby")) {
                  element.setAttribute(
                    "aria-describedby",
                    element.getAttribute("aria-describedby").replaceAll("__index__", index)
                  );
                }
                if (element.hasAttribute("data-valmsg-for")) {
                  element.setAttribute(
                    "data-valmsg-for",
                    element.getAttribute("data-valmsg-for").replaceAll("__index__", index)
                  );
                }
              });
              const row = fragment.querySelector("[data-row]");
              wireRemove(row);
              container.append(fragment);
              row.querySelector("input").focus();
            });

            repeaters.push(() => {
              container.innerHTML = initialMarkup;
              container.querySelectorAll("[data-row]").forEach(wireRemove);
            });
          });

          const loginForm = document.querySelector("form[data-provider]");
          let resetTenantTracking = () => {};
          if (loginForm?.dataset.provider === "aad") {
            const tenantInput = document.getElementById("Input_TenantId");
            const issuerInput = document.getElementById("Input_Issuer");
            let previousTenant = tenantInput.value;

            tenantInput.addEventListener("input", () => {
              const previousDefault =
                `https://login.microsoftonline.com/${previousTenant}/v2.0`;
              if (issuerInput.value === previousDefault) {
                issuerInput.value =
                  `https://login.microsoftonline.com/${tenantInput.value}/v2.0`;
              }
              previousTenant = tenantInput.value;
            });

            resetTenantTracking = () => {
              previousTenant = tenantInput.value;
            };
          }

          loginForm?.addEventListener("reset", () => {
            window.setTimeout(() => {
              repeaters.forEach((restore) => restore());
              const advanced = document.querySelector(".advanced");
              if (!advanced?.querySelector(".field-validation-error")) {
                advanced?.removeAttribute("open");
              }
              resetTenantTracking();
            }, 0);
          });
        })();
        """;
}
