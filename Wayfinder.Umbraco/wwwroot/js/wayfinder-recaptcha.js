// Fetches a reCAPTCHA v3 token just before a Wayfinder stage form is sent, then sends it with the button
// that was pressed (requestSubmit keeps the button's name and value, which the form needs).
(function () {
    const script = document.currentScript;
    const siteKey = script.dataset.recaptchaSiteKey;
    const action = script.dataset.recaptchaAction;

    document.querySelectorAll('input[data-wayfinder-recaptcha-token]').forEach(function (tokenInput) {
        const form = tokenInput.form;
        if (!form) {
            return;
        }

        let tokenReady = false;
        form.addEventListener('submit', function (event) {
            if (tokenReady) {
                return;
            }

            event.preventDefault();
            const submitter = event.submitter;
            if (submitter) {
                submitter.disabled = true;
            }

            const send = function () {
                if (tokenReady) {
                    return;
                }
                tokenReady = true;
                if (submitter) {
                    submitter.disabled = false;
                }
                // Next tick: a form ignores requestSubmit while its own submit event is still firing.
                setTimeout(function () {
                    form.requestSubmit(submitter);
                }, 0);
            };

            // Without a token (reCAPTCHA blocked or failing) the server turns it away with a message.
            if (typeof grecaptcha === 'undefined') {
                send();
                return;
            }

            // Never leave the visitor stuck if reCAPTCHA doesn't answer.
            setTimeout(send, 10000);
            grecaptcha.ready(function () {
                // execute() returns Google's own thenable, which has no catch() or finally(): wrap it.
                new Promise(function (resolve) {
                    resolve(grecaptcha.execute(siteKey, { action: action }));
                })
                    .then(function (token) {
                        tokenInput.value = token;
                    }, function () { })
                    .then(send);
            });
        });
    });
})();
