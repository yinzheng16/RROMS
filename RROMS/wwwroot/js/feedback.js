// =========================================================
// US42 Customer Feedback (Member 5)
// Rating stamps, character counter and quick phrases for
// Pages/Customer/Feedback/Create.cshtml. The order cards and
// stamps are real radio buttons, so the form works without this file.
// =========================================================
(function () {
    "use strict";

    var form = document.getElementById("fbForm");
    if (!form) {
        return;   // empty state: no completed orders to review
    }

    var LABELS = [
        ["残念", "Poor"],
        ["まあまあ", "Fair"],
        ["良い", "Good"],
        ["とても良い", "Very good"],
        ["最高", "Excellent"]
    ];

    // ---------- Rating stamps ----------
    var stamps = Array.prototype.slice.call(document.querySelectorAll(".fb-stamp"));
    var ratingText = document.getElementById("fbRatingText");

    function chosenRating() {
        var checked = form.querySelector('input[name="Rating"]:checked');
        return checked ? parseInt(checked.value, 10) : 0;
    }

    function paint(n) {
        stamps.forEach(function (stamp, i) {
            stamp.classList.toggle("is-on", i < n);
        });

        if (!ratingText) { return; }

        if (n > 0) {
            ratingText.innerHTML = "";
            var jp = document.createElement("b");
            jp.textContent = LABELS[n - 1][0];
            ratingText.appendChild(jp);
            ratingText.appendChild(document.createTextNode("  " + LABELS[n - 1][1] + " · " + n + " / 5"));
        } else {
            ratingText.textContent = "Tap a stamp to rate your night";
        }
    }

    stamps.forEach(function (stamp, i) {
        stamp.addEventListener("mouseenter", function () { paint(i + 1); });
        stamp.addEventListener("mouseleave", function () { paint(chosenRating()); });
        stamp.querySelector("input").addEventListener("change", function () { paint(chosenRating()); });
    });

    // ---------- Comment counter + quick phrases ----------
    var comment = document.getElementById("fbComment");
    var count = document.getElementById("fbCount");

    function updateCount() {
        if (comment && count) {
            count.textContent = comment.value.length;
        }
    }

    if (comment) {
        comment.addEventListener("input", updateCount);

        document.querySelectorAll(".fb-phrase").forEach(function (button) {
            button.addEventListener("click", function () {
                var text = button.textContent.trim() + ". ";
                var current = comment.value;
                var spacer = current && !/\s$/.test(current) ? " " : "";

                if (current.length + spacer.length + text.length <= 500) {
                    comment.value = current + spacer + text;
                    updateCount();
                    comment.focus();
                }
            });
        });
    }

    // ---------- First paint (keeps choices after a validation error) ----------
    paint(chosenRating());
    updateCount();
})();
