/* Shared Contoso portal behavior.
   Keep page-specific wiring in page or form scripts and put small reusable helpers here. */
(function(window, $) {
  "use strict";

  window.ContosoCommon = window.ContosoCommon || {};

  /* Keep inline validation styling and accessibility attributes in one place
     so blur validation and submit validation behave identically. */
  function setFieldValidationState(input, error, isValid, validationName) {
    var states = input.data("contoso-validation-states") || {};
    var hasInvalidState;

    states[validationName || "default"] = isValid;
    input.data("contoso-validation-states", states);

    hasInvalidState = Object.keys(states).some(function(key) {
      return states[key] === false;
    });

    input.toggleClass("contoso-input-invalid", hasInvalidState);
    input.attr("aria-invalid", hasInvalidState ? "true" : "false");
    error.toggle(!isValid);
  }

  /* Power Pages renders validation summary messages as links that call
     scrollToAndFocus. Match that native pattern for custom validators. */
  function buildFieldValidationMessage(controlId, message) {
    var labelId = controlId + "_label";
    return "<a href='#" + labelId + "' onclick='javascript:scrollToAndFocus(\"" + labelId + "\",\"" + controlId + "\");return false;' referenceControlId=" + controlId + ">" + message + "</a>";
  }

  /* Add our inline error to aria-describedby without removing descriptions
     that Dataverse or Power Pages may already have placed on the field. */
  function addDescribedBy(input, describedById) {
    var existingValue = input.attr("aria-describedby") || "";
    var ids = existingValue.split(/\s+/).filter(Boolean);

    if ($.inArray(describedById, ids) === -1) {
      ids.push(describedById);
      input.attr("aria-describedby", ids.join(" "));
    }
  }

  function getFieldLabel(input) {
    var controlId = input.attr("id");
    var label = controlId ? $("#" + controlId + "_label") : $();
    var labelText = $.trim(label.text());

    return labelText || input.attr("aria-label") || input.attr("name") || "This field";
  }

  function getFieldValue(input) {
    if (input.is(":checkbox")) {
      return input.is(":checked") ? input.val() : "";
    }

    if (input.is(":radio")) {
      return $("input[name='" + input.attr("name") + "']:checked").val() || "";
    }

    return $.trim(input.val() || "");
  }

  function ensureInlineError(input, errorId, message) {
    var error = $("#" + errorId);

    if (!error.length) {
      error = $("<div>", {
        id: errorId,
        class: "contoso-field-error",
        role: "alert",
        text: message
      }).hide();

      input.after(error);
    }

    return error;
  }

  function markGeneratedFieldRequired(input) {
    var controlId = input.attr("id");
    var label = controlId ? $("#" + controlId + "_label") : $();
    var infoContainer = label.closest(".table-info, .info");

    if (!infoContainer.length) {
      infoContainer = input.closest("td.cell").find(".table-info, .info").first();
    }

    infoContainer.addClass("required");
    input.attr("aria-required", "true");
  }

  /* Register a client validator with the Power Pages Page_Validators array.
     This blocks submit, feeds the standard validation summary, and reuses the
     same formatting and inline state used by the field blur handlers. */
  function addPageValidator(input, validatorName, error, message, isValidFieldValue, formatFieldValue) {
    var controlId = input.attr("id");
    var validatorId = controlId + "_contoso_" + validatorName + "_validator";

    /* Page_Validators only exists on generated Power Pages forms. If this
       helper runs on a plain content page, keep the mask behavior only. */
    if (!controlId || !window.Page_Validators || !window.Page_Validators.push) {
      return;
    }

    /* Avoid duplicate validators with the same Contoso validator name if a
       page script calls the helper twice for the same field. */
    for (var i = 0; i < window.Page_Validators.length; i++) {
      if (window.Page_Validators[i].id === validatorId) {
        return;
      }
    }

    var validator = document.createElement("span");
    validator.id = validatorId;
    validator.style.display = "none";
    validator.display = "Dynamic";
    validator.initialvalue = "";
    validator.controltovalidate = controlId;
    validator.errormessage = buildFieldValidationMessage(controlId, message);
    validator.evaluationfunction = function() {
      if (formatFieldValue) {
        input.val(formatFieldValue(input.val()));
      }

      var isValid = isValidFieldValue(input);
      setFieldValidationState(input, error, isValid, validatorName);
      return isValid;
    };

    window.Page_Validators.push(validator);
  }

  window.ContosoCommon.addBackToHomeButton = function(options) {
    var settings = $.extend({
      beforeSelector: "#NextButton, #SubmitButton, #UpdateButton, .actions input[type='submit'], .actions .btn-primary",
      buttonId: "contoso-back-home-button",
      buttonText: "Back to Home",
      homeUrl: "/"
    }, options || {});

    var actionButton = $(settings.beforeSelector).first();
    if (!actionButton.length || $("#" + settings.buttonId).length) {
      return;
    }

    var backHomeButton = $("<button>", {
      id: settings.buttonId,
      type: "button",
      class: "btn btn-secondary",
      text: settings.buttonText
    });
    var backHomeButtonGroup = $("<div>", {
      role: "group",
      class: "btn-group entity-action-button contoso-back-home-button-group"
    }).append(backHomeButton);
    var generatedButtonGroup = actionButton.closest(".btn-group.entity-action-button");

    backHomeButton.on("click", function() {
      window.location.href = settings.homeUrl;
    });

    /* Advanced forms wrap generated buttons in Bootstrap button groups. Insert
       the Back button as its own group so the action-row flex gap can space it
       consistently from Next, Previous, and Submit buttons. */
    if (generatedButtonGroup.length) {
      generatedButtonGroup.before(backHomeButtonGroup);
      return;
    }

    actionButton.before(backHomeButtonGroup);
  };

  /* Add client-side required behavior to generated Power Pages fields.
     This is useful when a portal form needs a stricter requirement than the
     Dataverse form currently provides. */
  window.ContosoCommon.makeRequired = function(selector, options) {
    var settings = $.extend({
      message: null
    }, options || {});

    $(selector).each(function() {
      var input = $(this);
      var controlId = input.attr("id");
      var fieldName = getFieldLabel(input);
      var errorMessage = settings.message || fieldName + " is a required field.";
      var errorId = controlId + "_contoso_required_validation";
      var error;

      if (!controlId) {
        return;
      }

      error = ensureInlineError(input, errorId, errorMessage);
      markGeneratedFieldRequired(input);
      addDescribedBy(input, errorId);

      input.on("input change blur", function() {
        setFieldValidationState(input, error, !!getFieldValue(input), "required");
      });

      addPageValidator(input, "required", error, errorMessage, function(field) {
        return !!getFieldValue(field);
      });
    });
  };

  /* Format North American phone numbers while allowing blank values.
     Invalid partial values are shown inline and blocked on form submit. */
  window.ContosoCommon.formatPhoneInput = function(selector, options) {
    var settings = $.extend({
      maxDigits: 10,
      errorMessage: "Enter a 10-digit phone number, such as (123) 123-1234."
    }, options || {});

    function getPhoneDigits(value) {
      return (value || "").replace(/\D/g, "");
    }

    function getNormalizedPhoneDigits(value) {
      var digits = getPhoneDigits(value);

      /* Accept pasted +1 / 1-prefixed numbers, then store the local 10 digits
         in the same display format as manually entered values. */
      if (digits.length === 11 && digits.charAt(0) === "1") {
        return digits.slice(1);
      }

      return digits;
    }

    function formatPhoneNumber(value) {
      var digits = getNormalizedPhoneDigits(value).slice(0, settings.maxDigits);

      if (digits.length <= 3) {
        return digits ? "(" + digits : "";
      }

      if (digits.length <= 6) {
        return "(" + digits.slice(0, 3) + ") " + digits.slice(3);
      }

      return "(" + digits.slice(0, 3) + ") " + digits.slice(3, 6) + "-" + digits.slice(6);
    }

    function isValidPhoneNumber(value) {
      var digits = getNormalizedPhoneDigits(value);
      return !digits.length || digits.length === settings.maxDigits;
    }

    $(selector).each(function() {
      var input = $(this);
      var controlId = input.attr("id");
      var errorId = controlId + "_contoso_validation";
      var error;

      if (!controlId) {
        return;
      }

      error = ensureInlineError(input, errorId, settings.errorMessage);

      /* Mobile keyboard and browser autofill hints only; validation still runs
         through our handlers and the Power Pages Page_Validators entry. */
      input.attr("inputmode", "tel");
      input.attr("autocomplete", input.attr("autocomplete") || "tel");
      addDescribedBy(input, errorId);

      /* While typing, format optimistically and clear the error. The final
         validity decision is made on blur and again during submit. */
      input.on("input", function() {
        this.value = formatPhoneNumber(this.value);
        setFieldValidationState(input, error, true, "format");
      });

      input.on("blur", function() {
        this.value = formatPhoneNumber(this.value);
        setFieldValidationState(input, error, isValidPhoneNumber(this.value), "format");
      });

      /* Add the submit-time validator after the field is prepared so the
         validation summary and inline error use the same message. */
      addPageValidator(input, "format", error, settings.errorMessage, function(field) {
        return isValidPhoneNumber(field.val());
      }, formatPhoneNumber);

      if (input.val()) {
        input.val(formatPhoneNumber(input.val()));
        input.trigger("blur");
      }
    });
  };

  /* Format Canadian postal codes as A1A 1A1 while allowing blank values.
     Invalid values are shown inline and blocked on form submit. */
  window.ContosoCommon.formatPostalCodeInput = function(selector, options) {
    var settings = $.extend({
      errorMessage: "Enter a valid postal code, such as K1A 0B1."
    }, options || {});

    function formatPostalCode(value) {
      var characters = (value || "").toUpperCase().replace(/[^A-Z0-9]/g, "").slice(0, 6);

      if (characters.length <= 3) {
        return characters;
      }

      return characters.slice(0, 3) + " " + characters.slice(3);
    }

    function isValidPostalCode(value) {
      return !value || /^[A-Z]\d[A-Z] \d[A-Z]\d$/.test(value);
    }

    $(selector).each(function() {
      var input = $(this);
      var controlId = input.attr("id");
      var errorId = controlId + "_contoso_validation";
      var error;

      if (!controlId) {
        return;
      }

      error = ensureInlineError(input, errorId, settings.errorMessage);

      /* Use a text keyboard hint because Canadian postal codes are alphanumeric. */
      input.attr("inputmode", "text");
      input.attr("autocomplete", input.attr("autocomplete") || "postal-code");
      addDescribedBy(input, errorId);

      /* Keep the user's input normalized as they type, but do not show a red
         error until they leave the field or attempt to submit. */
      input.on("input", function() {
        this.value = formatPostalCode(this.value);
        setFieldValidationState(input, error, true, "format");
      });

      input.on("blur", function() {
        this.value = formatPostalCode(this.value);
        setFieldValidationState(input, error, isValidPostalCode(this.value), "format");
      });

      /* Submit-time validator keeps bad postal codes from saving even if the
         field was never blurred before the user clicked submit. */
      addPageValidator(input, "format", error, settings.errorMessage, function(field) {
        return isValidPostalCode(field.val());
      }, formatPostalCode);

      if (input.val()) {
        input.val(formatPostalCode(input.val()));
        input.trigger("blur");
      }
    });
  };
})(window, window.jQuery);
