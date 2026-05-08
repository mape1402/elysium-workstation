using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;
using Microsoft.Maui.Controls;

namespace Elysium.WorkStation.Views
{
    [QueryProperty(nameof(LinkIdQuery), "id")]
    public partial class RemoteToolsPage : ContentPage
    {
        private readonly IFolderSyncService _folderSyncService;
        private int _linkId;
        private string _syncId = string.Empty;
        private readonly string _sessionId = Guid.NewGuid().ToString("N");
        private bool _terminalReady;
        private bool _commandInFlight;
        private bool _isWebViewLoading = true;

        public string LinkIdQuery
        {
            set
            {
                if (int.TryParse(value, out var parsed))
                {
                    _linkId = parsed;
                }
            }
        }

        public string HeaderText => $"remote@sync-{_linkId}:~";
        public bool IsWebViewLoading => _isWebViewLoading;
        public Command ClearTerminalCommand { get; }

        public RemoteToolsPage(IFolderSyncService folderSyncService)
        {
            _folderSyncService = folderSyncService;

            ClearTerminalCommand = new Command(async () =>
            {
                if (!_terminalReady)
                {
                    return;
                }

                SetWebViewLoading(true);
                await SetTerminalHtmlAsync(BuildTerminalHtml(HeaderText, IsDarkTheme()));
            });
            InitializeComponent();
            BindingContext = this;
            TerminalWebView.Source = new HtmlWebViewSource
            {
                Html = BuildTerminalHtml(HeaderText, IsDarkTheme())
            };
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            _folderSyncService.RemoteTerminalOutputReceived -= OnRemoteTerminalOutputReceived;
            _folderSyncService.RemoteTerminalOutputReceived += OnRemoteTerminalOutputReceived;
            await _folderSyncService.ReloadAsync();

            var link = _folderSyncService.Links.FirstOrDefault(item => item.Id == _linkId);
            _syncId = link?.SyncId ?? string.Empty;
            OnPropertyChanged(nameof(HeaderText));

            _commandInFlight = false;
            SetWebViewLoading(true);
            await SetTerminalHtmlAsync(BuildTerminalHtml(HeaderText, IsDarkTheme()));
            _terminalReady = true;
        }

        protected override void OnDisappearing()
        {
            _folderSyncService.RemoteTerminalOutputReceived -= OnRemoteTerminalOutputReceived;
            base.OnDisappearing();
        }

        private async Task SetTerminalHtmlAsync(string html)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                TerminalWebView.Source = new HtmlWebViewSource
                {
                    Html = html
                };
            });
        }

        private async void OnTerminalNavigating(object sender, WebNavigatingEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Url) && e.Url.StartsWith("termready://", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                SetWebViewLoading(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(e.Url) || !e.Url.StartsWith("termcmd://", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            e.Cancel = true;

            if (_commandInFlight)
            {
                await EvalJsAsync("termAppendLine('[busy] Espera a que termine el comando actual.', 'muted'); termResetPrompt();");
                return;
            }

            var command = string.Empty;
            if (Uri.TryCreate(e.Url, UriKind.Absolute, out var uri))
            {
                var rawQuery = uri.Query?.TrimStart('?') ?? string.Empty;
                var parts = rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2 && string.Equals(kv[0], "cmd", StringComparison.OrdinalIgnoreCase))
                    {
                        command = Uri.UnescapeDataString(kv[1]);
                        break;
                    }
                }
            }
            else
            {
                var encoded = e.Url["termcmd://".Length..];
                command = Uri.UnescapeDataString(encoded ?? string.Empty);
            }

            command = (command ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(command))
            {
                await EvalJsAsync("termResetPrompt();");
                return;
            }

            _commandInFlight = true;
            await EvalJsAsync("termSetLocked(true);");
            try
            {
                await _folderSyncService.SendRemoteTerminalCommandAsync(_linkId, _sessionId, command);
            }
            catch (Exception ex)
            {
                await EvalJsAsync($"termAppendLine({ToJsString("[error] " + ex.Message)}, 'error'); termSetLocked(false); termResetPrompt();");
                _commandInFlight = false;
            }
        }

        private void OnRemoteTerminalOutputReceived(object sender, RemoteTerminalOutputEventArgs e)
        {
            if (e is null)
            {
                return;
            }

            if (!string.Equals(e.SyncId, _syncId, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.Equals(e.SessionId, _sessionId, StringComparison.Ordinal))
            {
                return;
            }

            _ = MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (!_terminalReady)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(e.Chunk))
                {
                    var level = e.IsError ? "error" : "out";
                    await EvalJsAsync($"termAppendLine({ToJsString(e.Chunk)}, '{level}');");
                }

                if (e.IsCompleted)
                {
                    await EvalJsAsync($"termAppendLine({ToJsString($"[exit {e.ExitCode}]")}, 'muted'); termSetLocked(false); termResetPrompt();");
                    _commandInFlight = false;
                }
            });
        }

        private async Task EvalJsAsync(string js)
        {
            try
            {
                await TerminalWebView.EvaluateJavaScriptAsync(js);
            }
            catch
            {
                // Ignore transient script errors during reload.
            }
        }

        private void SetWebViewLoading(bool loading)
        {
            if (_isWebViewLoading == loading)
            {
                return;
            }

            _isWebViewLoading = loading;
            OnPropertyChanged(nameof(IsWebViewLoading));
        }

        private static string ToJsString(string value)
        {
            var text = value ?? string.Empty;
            text = text
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
            return $"'{text}'";
        }

        private bool IsDarkTheme()
        {
            return Application.Current?.RequestedTheme == AppTheme.Dark;
        }

        private static string BuildTerminalHtml(string header, bool isDarkTheme)
        {
            var safeHeader = System.Net.WebUtility.HtmlEncode(header ?? "remote@sync");
            var themeClass = isDarkTheme ? "dark" : "light";
            return $$"""
<!doctype html>
<html>
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <style>
    html, body {
      margin: 0;
      padding: 0;
      width: 100%;
      height: 100%;
      font-family: Consolas, "Courier New", monospace;
      overflow: hidden;
    }
    body.dark {
      --term-bg: #010409;
      --term-fg: #c9d1d9;
      --title-bg: #161b22;
      --title-fg: #8b949e;
      --title-border: #2a2f3a;
      --out: #c9d1d9;
      --error: #ff7b72;
      --muted: #8b949e;
      --accent: #58a6ff;
    }
    body.light {
      --term-bg: #ffffff;
      --term-fg: #1f2937;
      --title-bg: #eef3f8;
      --title-fg: #4b5563;
      --title-border: #cdd6e2;
      --out: #1f2937;
      --error: #b42318;
      --muted: #667085;
      --accent: #175cd3;
    }
    body {
      background: var(--term-bg);
      color: var(--term-fg);
    }
    .wrap {
      display: flex;
      flex-direction: column;
      width: 100%;
      height: 100%;
    }
    .title {
      flex: 0 0 auto;
      background: var(--title-bg);
      border-bottom: 1px solid var(--title-border);
      padding: 8px 10px;
      color: var(--title-fg);
      font-size: 12px;
    }
    #term {
      flex: 1 1 auto;
      overflow: auto;
      padding: 10px;
      font-size: 12px;
      line-height: 1.45;
      white-space: pre-wrap;
      word-break: break-word;
      outline: none;
    }
    .line { margin: 0; }
    .out { color: var(--out); }
    .error { color: var(--error); }
    .muted { color: var(--muted); }
    .cmd { color: var(--accent); }
    .prompt { color: var(--accent); }
    #inputLine { display: inline; }
    #inputText {
      display: inline;
      outline: none;
      border: none;
      background: transparent;
      color: var(--term-fg);
      min-width: 2ch;
      caret-color: var(--term-fg);
    }
    .ansi-bold { font-weight: 700; }
    .ansi-black { color: #484f58; }
    .ansi-red { color: #ff7b72; }
    .ansi-green { color: #3fb950; }
    .ansi-yellow { color: #d29922; }
    .ansi-blue { color: #58a6ff; }
    .ansi-magenta { color: #bc8cff; }
    .ansi-cyan { color: #39c5cf; }
    .ansi-white { color: #c9d1d9; }
    .ansi-bright-black { color: #8b949e; }
    .ansi-bright-red { color: #ffa198; }
    .ansi-bright-green { color: #56d364; }
    .ansi-bright-yellow { color: #e3b341; }
    .ansi-bright-blue { color: #79c0ff; }
    .ansi-bright-magenta { color: #d2a8ff; }
    .ansi-bright-cyan { color: #56d4dd; }
    .ansi-bright-white { color: #f0f6fc; }
    .ansi-bg-black { background: #0d1117; }
    .ansi-bg-red { background: #8e1519; }
    .ansi-bg-green { background: #1f6f3d; }
    .ansi-bg-yellow { background: #7f5d00; }
    .ansi-bg-blue { background: #0b3d91; }
    .ansi-bg-magenta { background: #5a1e9a; }
    .ansi-bg-cyan { background: #1b7f83; }
    .ansi-bg-white { background: #6e7681; color: #0d1117; }
    .ansi-bg-bright-black { background: #30363d; }
    .ansi-bg-bright-red { background: #b62324; }
    .ansi-bg-bright-green { background: #238636; }
    .ansi-bg-bright-yellow { background: #9a6700; }
    .ansi-bg-bright-blue { background: #1f6feb; }
    .ansi-bg-bright-magenta { background: #8250df; }
    .ansi-bg-bright-cyan { background: #1f6f78; }
    .ansi-bg-bright-white { background: #f0f6fc; color: #0d1117; }
  </style>
</head>
<body class="{{themeClass}}">
  <div class="wrap">
    <div class="title">o {{safeHeader}}</div>
    <div id="term" tabindex="0"></div>
  </div>

  <script>
    const term = document.getElementById('term');
    let locked = false;
    const commandHistory = [];
    let historyIndex = -1;

    function scrollBottom() {
      term.scrollTop = term.scrollHeight;
    }

    function escapeHtml(raw) {
      const map = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
      return (raw || '').replace(/[&<>"']/g, (ch) => map[ch]);
    }

    function ansiColorClass(code, isBackground) {
      const fg = { 30:'ansi-black',31:'ansi-red',32:'ansi-green',33:'ansi-yellow',34:'ansi-blue',35:'ansi-magenta',36:'ansi-cyan',37:'ansi-white',90:'ansi-bright-black',91:'ansi-bright-red',92:'ansi-bright-green',93:'ansi-bright-yellow',94:'ansi-bright-blue',95:'ansi-bright-magenta',96:'ansi-bright-cyan',97:'ansi-bright-white' };
      const bg = { 40:'ansi-bg-black',41:'ansi-bg-red',42:'ansi-bg-green',43:'ansi-bg-yellow',44:'ansi-bg-blue',45:'ansi-bg-magenta',46:'ansi-bg-cyan',47:'ansi-bg-white',100:'ansi-bg-bright-black',101:'ansi-bg-bright-red',102:'ansi-bg-bright-green',103:'ansi-bg-bright-yellow',104:'ansi-bg-bright-blue',105:'ansi-bg-bright-magenta',106:'ansi-bg-bright-cyan',107:'ansi-bg-bright-white' };
      return (isBackground ? bg[code] : fg[code]) || '';
    }

    function ansiToHtml(input) {
      const re = /\u001b\[([0-9;]*)m/g;
      let out = '';
      let last = 0;
      let currentClasses = [];
      let match;
      const text = input || '';

      while ((match = re.exec(text)) !== null) {
        const chunk = text.slice(last, match.index);
        if (chunk) {
          const escaped = escapeHtml(chunk);
          out += currentClasses.length ? `<span class="${currentClasses.join(' ')}">${escaped}</span>` : escaped;
        }

        const codes = (match[1] || '0').split(';').map(x => parseInt(x || '0', 10));
        if (codes.includes(0)) {
          currentClasses = [];
        }

        for (const code of codes) {
          if (Number.isNaN(code) || code === 0) continue;
          if (code === 1) {
            if (!currentClasses.includes('ansi-bold')) currentClasses.push('ansi-bold');
            continue;
          }

          const fgClass = ansiColorClass(code, false);
          if (fgClass) {
            currentClasses = currentClasses.filter(c => !c.startsWith('ansi-') || c.startsWith('ansi-bg-') || c === 'ansi-bold');
            currentClasses.push(fgClass);
            continue;
          }

          const bgClass = ansiColorClass(code, true);
          if (bgClass) {
            currentClasses = currentClasses.filter(c => !c.startsWith('ansi-bg-'));
            currentClasses.push(bgClass);
          }
        }

        last = re.lastIndex;
      }

      const tail = text.slice(last);
      if (tail) {
        const escaped = escapeHtml(tail);
        out += currentClasses.length ? `<span class="${currentClasses.join(' ')}">${escaped}</span>` : escaped;
      }

      return out || '';
    }

    function line(text, css) {
      const div = document.createElement('div');
      div.className = 'line ' + css;
      div.innerHTML = ansiToHtml(text || '');
      term.appendChild(div);
      scrollBottom();
    }

    function currentInputSpan() {
      return document.getElementById('inputText');
    }

    function ensurePrompt() {
      const old = document.getElementById('inputLine');
      if (old) old.remove();

      const promptLine = document.createElement('div');
      promptLine.className = 'line';
      promptLine.id = 'inputLine';

      const prompt = document.createElement('span');
      prompt.className = 'prompt';
      prompt.textContent = 'PS> ';
      promptLine.appendChild(prompt);

      const input = document.createElement('span');
      input.id = 'inputText';
      input.contentEditable = locked ? 'false' : 'true';
      input.spellcheck = false;
      input.autocorrect = 'off';
      input.autocapitalize = 'off';
      promptLine.appendChild(input);

      term.appendChild(promptLine);
      scrollBottom();
      if (!locked) {
        placeCursorAtEnd(input);
      }
    }

    function placeCursorAtEnd(el) {
      const range = document.createRange();
      range.selectNodeContents(el);
      range.collapse(false);
      const sel = window.getSelection();
      sel.removeAllRanges();
      sel.addRange(range);
      el.focus();
    }

    function submitCurrent() {
      if (locked) return;
      const input = currentInputSpan();
      if (!input) return;
      const promptLine = document.getElementById('inputLine');
      const cmd = input.textContent || '';
      const trimmed = cmd.trim();
      const lower = trimmed.toLowerCase();

      if (trimmed.length > 0) {
        commandHistory.push(cmd);
      }
      historyIndex = commandHistory.length;

      input.contentEditable = 'false';
      input.className = 'cmd';
      if (promptLine) {
        promptLine.removeAttribute('id');
      }
      input.removeAttribute('id');

      if (lower === 'cls' || lower === 'clean') {
        clearTerminalViewport();
      }

      locked = true;
      window.location.href = 'termcmd://run?cmd=' + encodeURIComponent(cmd) + '&nonce=' + Date.now().toString();
    }

    function clearTerminalViewport() {
      while (term.firstChild) {
        term.removeChild(term.firstChild);
      }
    }

    function setInputText(text) {
      const input = currentInputSpan();
      if (!input) return;
      input.textContent = text || '';
      placeCursorAtEnd(input);
    }

    term.addEventListener('keydown', (e) => {
      const input = currentInputSpan();
      if (!input) return;
      if (e.key === 'Enter') {
        e.preventDefault();
        submitCurrent();
        return;
      }
      if (e.key === 'ArrowUp') {
        e.preventDefault();
        if (!commandHistory.length) return;
        historyIndex = Math.max(0, historyIndex - 1);
        setInputText(commandHistory[historyIndex]);
        return;
      }
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        if (!commandHistory.length) return;
        historyIndex = Math.min(commandHistory.length, historyIndex + 1);
        if (historyIndex >= commandHistory.length) {
          setInputText('');
          return;
        }
        setInputText(commandHistory[historyIndex]);
        return;
      }
      if (locked) {
        const key = (e.key || '').toLowerCase();
        if ((e.ctrlKey || e.metaKey) && (key === 'c' || key === 'a')) {
          return;
        }
        e.preventDefault();
      }
    });

    window.termAppendLine = function(text, level) {
      const css = level === 'error' ? 'error' : (level === 'muted' ? 'muted' : 'out');
      line(text || '', css);
    }

    window.termSetLocked = function(v) {
      locked = !!v;
      const input = currentInputSpan();
      if (input) {
        input.contentEditable = locked ? 'false' : 'true';
      }
    }

    window.termResetPrompt = function() {
      ensurePrompt();
    }

    line('Remote terminal ready.', 'muted');
    ensurePrompt();
    window.location.href = 'termready://ready';
  </script>
</body>
</html>
""";
        }
    }
}
