mergeInto(LibraryManager.library, {
  ShopNamingWebInput_Create: function (receiverPtr, maximumLength, playerValuePtr,
    rivalValuePtr, playerPlaceholderPtr, rivalPlaceholderPtr) {
    if (window.__shopNamingWebInput) window.__shopNamingWebInput.destroy();

    var receiver = UTF8ToString(receiverPtr);
    var canvas = Module.canvas;
    if (!canvas) return;

    var state = {
      receiver: receiver,
      canvas: canvas,
      maximumLength: maximumLength,
      inputs: [],
      listeners: [],
      destroy: function () {
        for (var i = 0; i < this.listeners.length; i++) {
          var entry = this.listeners[i];
          entry.target.removeEventListener(entry.event, entry.handler, entry.options || false);
        }
        for (var j = 0; j < this.inputs.length; j++) {
          var input = this.inputs[j];
          if (input && input.parentNode) input.parentNode.removeChild(input);
        }
        this.listeners = [];
        this.inputs = [];
        if (window.__shopNamingWebInput === this) window.__shopNamingWebInput = null;
      }
    };

    function listen(target, event, handler, options) {
      target.addEventListener(event, handler, options || false);
      state.listeners.push({ target: target, event: event, handler: handler, options: options });
    }

    function positionInputs() {
      if (!state.canvas || !state.canvas.isConnected) return;
      var rect = state.canvas.getBoundingClientRect();
      var specs = [
        { left: 665, top: 448, width: 590, height: 76 },
        { left: 665, top: 584, width: 590, height: 76 }
      ];
      for (var i = 0; i < state.inputs.length; i++) {
        var input = state.inputs[i];
        var spec = specs[i];
        input.style.left = (rect.left + rect.width * spec.left / 1920) + 'px';
        input.style.top = (rect.top + rect.height * spec.top / 1080) + 'px';
        input.style.width = (rect.width * spec.width / 1920) + 'px';
        input.style.height = (rect.height * spec.height / 1080) + 'px';
        input.style.fontSize = Math.max(16, rect.height * 28 / 1080) + 'px';
      }
    }

    function makeInput(value, placeholder, method, index) {
      function clampValue(text) {
        return Array.from(text || '').slice(0, maximumLength).join('');
      }
      var input = document.createElement('input');
      input.type = 'text';
      input.value = clampValue(value);
      // Unity already renders the placeholder below this transparent HTML input.
      // Keeping a browser placeholder here draws the same text twice.
      input.placeholder = '';
      input.maxLength = maximumLength;
      input.autocomplete = 'off';
      input.autocapitalize = 'none';
      input.spellcheck = false;
      input.setAttribute('aria-label', placeholder);
      input.dataset.shopNamingIndex = index.toString();
      input.style.position = 'fixed';
      input.style.zIndex = '2147483646';
      input.style.boxSizing = 'border-box';
      input.style.margin = '0';
      input.style.padding = '0';
      input.style.border = '0';
      input.style.outline = 'none';
      input.style.background = 'transparent';
      input.style.color = '#4b3021';
      input.style.caretColor = '#4b3021';
      input.style.fontFamily = '"Noto Sans KR", "Malgun Gothic", sans-serif';
      input.style.fontWeight = '600';
      input.style.lineHeight = '1';

      listen(input, 'input', function () {
        input.value = clampValue(input.value);
        if (typeof SendMessage === 'function') SendMessage(state.receiver, method, input.value);
      });
      listen(input, 'keydown', function (event) {
        event.stopPropagation();
        if (event.key !== 'Enter' || event.isComposing) return;
        event.preventDefault();
        if (index === 0 && state.inputs[1]) state.inputs[1].focus();
        else if (typeof SendMessage === 'function') SendMessage(state.receiver, 'HandleWebNamingSubmit', '');
      });
      listen(input, 'keyup', function (event) { event.stopPropagation(); });
      listen(input, 'compositionstart', function (event) { event.stopPropagation(); });
      listen(input, 'compositionupdate', function (event) { event.stopPropagation(); });
      listen(input, 'compositionend', function (event) {
        event.stopPropagation();
        input.value = clampValue(input.value);
        if (typeof SendMessage === 'function') SendMessage(state.receiver, method, input.value);
      });
      document.body.appendChild(input);
      state.inputs.push(input);
    }

    makeInput(UTF8ToString(playerValuePtr), UTF8ToString(playerPlaceholderPtr),
      'HandleWebPlayerNameChanged', 0);
    makeInput(UTF8ToString(rivalValuePtr), UTF8ToString(rivalPlaceholderPtr),
      'HandleWebRivalNameChanged', 1);
    listen(window, 'resize', positionInputs);
    listen(window, 'scroll', positionInputs, true);
    positionInputs();
    window.__shopNamingWebInput = state;
    setTimeout(function () {
      positionInputs();
      if (state.inputs[0]) state.inputs[0].focus();
    }, 0);
  },

  ShopNamingWebInput_SetValues: function (playerValuePtr, rivalValuePtr) {
    var state = window.__shopNamingWebInput;
    if (!state || state.inputs.length < 2) return;
    state.inputs[0].value = Array.from(UTF8ToString(playerValuePtr) || '')
      .slice(0, state.maximumLength).join('');
    state.inputs[1].value = Array.from(UTF8ToString(rivalValuePtr) || '')
      .slice(0, state.maximumLength).join('');
  },

  ShopNamingWebInput_Destroy: function () {
    if (window.__shopNamingWebInput) window.__shopNamingWebInput.destroy();
  }
});
