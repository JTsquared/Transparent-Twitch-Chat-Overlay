// Blaze Chat Overlay Client
// Connects to the Blaze Stream API to display chat messages in a transparent overlay.

(function () {
    'use strict';

    const MAX_MESSAGES = 50;
    const POLL_INTERVAL_MS = 2000;
    const FADE_TIMEOUT_MS = 0; // 0 = no fade, set from config
    const API_BASE = 'https://api.blaze.stream/v1';

    let config = {
        channelId: '',
        clientId: '',
        accessToken: '',
        fadeTimeout: 0
    };

    let lastMessageId = null;
    let pollTimer = null;
    let knownMessageIds = new Set();
    const blazeChatHost = window.chrome && window.chrome.webview ? window.chrome.webview : null;

    // --- Status display ---

    function showStatus(text, duration) {
        const el = document.getElementById('status_text');
        el.textContent = text;
        el.classList.add('visible');
        if (duration > 0) {
            setTimeout(() => el.classList.remove('visible'), duration);
        }
    }

    function hideStatus() {
        document.getElementById('status_text').classList.remove('visible');
    }

    // --- Chat rendering ---

    function escapeHtml(str) {
        const div = document.createElement('div');
        div.appendChild(document.createTextNode(str));
        return div.innerHTML;
    }

    function getUserColor(username) {
        // Generate a consistent color from the username
        let hash = 0;
        for (let i = 0; i < username.length; i++) {
            hash = username.charCodeAt(i) + ((hash << 5) - hash);
        }
        const hue = Math.abs(hash) % 360;
        return `hsl(${hue}, 70%, 65%)`;
    }

    function renderBadges(roles) {
        let html = '';
        if (!roles) return html;

        if (roles.includes('moderator')) {
            html += '<span class="badge badge-mod" title="Moderator">M</span>';
        }
        if (roles.includes('vip')) {
            html += '<span class="badge badge-vip" title="VIP">V</span>';
        }
        if (roles.includes('og')) {
            html += '<span class="badge badge-og" title="OG">OG</span>';
        }
        if (roles.includes('subscriber')) {
            html += '<span class="badge badge-sub" title="Subscriber">S</span>';
        }
        return html;
    }

    function addChatMessage(msg) {
        const container = document.getElementById('chat_container');

        const displayName = msg.senderDisplayName || msg.senderUsername || 'Anonymous';
        const color = msg.senderColor || getUserColor(displayName);
        const badges = renderBadges(msg.senderRoles);
        const text = escapeHtml(msg.message || '');

        const line = document.createElement('div');
        line.className = 'chat-line';
        line.dataset.messageId = msg.id || '';
        line.innerHTML =
            badges +
            '<span class="username" style="color:' + color + '">' + escapeHtml(displayName) + '</span>' +
            '<span class="separator">: </span>' +
            '<span class="message-text">' + text + '</span>';

        container.appendChild(line);

        // Prune old messages
        while (container.children.length > MAX_MESSAGES) {
            container.removeChild(container.firstChild);
        }

        // Scroll to bottom
        container.scrollTop = container.scrollHeight;

        // Fade out after timeout
        if (config.fadeTimeout > 0) {
            setTimeout(() => {
                line.classList.add('fading');
                setTimeout(() => {
                    if (line.parentNode) {
                        line.parentNode.removeChild(line);
                    }
                }, 1000);
            }, config.fadeTimeout * 1000);
        }
    }

    // --- Blaze API ---

    async function fetchMessages() {
        if (!config.channelId || !config.clientId || !config.accessToken) {
            return;
        }

        try {
            let url = API_BASE + '/chats?channelId=' + encodeURIComponent(config.channelId) + '&limit=50';
            if (lastMessageId) {
                url += '&cursor=' + encodeURIComponent(lastMessageId);
            }

            const response = await fetch(url, {
                headers: {
                    'Authorization': 'Bearer ' + config.accessToken,
                    'client-id': config.clientId,
                    'Accept': 'application/json'
                }
            });

            if (!response.ok) {
                if (response.status === 401) {
                    showStatus('Authentication failed. Check your Blaze token.', 5000);
                }
                return;
            }

            const data = await response.json();
            const messages = data.messages || data || [];

            if (!Array.isArray(messages)) return;

            // Process new messages (oldest first)
            const newMessages = messages.filter(m => !knownMessageIds.has(m.id));
            newMessages.forEach(msg => {
                knownMessageIds.add(msg.id);
                addChatMessage(msg);
            });

            // Update cursor for next poll
            if (messages.length > 0) {
                lastMessageId = messages[messages.length - 1].id;
            }

            // Keep the known IDs set from growing unbounded
            if (knownMessageIds.size > 500) {
                const arr = Array.from(knownMessageIds);
                knownMessageIds = new Set(arr.slice(arr.length - 200));
            }
        } catch (err) {
            console.error('Blaze chat poll error:', err);
        }
    }

    function startPolling() {
        if (pollTimer) clearInterval(pollTimer);
        fetchMessages(); // initial fetch
        pollTimer = setInterval(fetchMessages, POLL_INTERVAL_MS);
    }

    function stopPolling() {
        if (pollTimer) {
            clearInterval(pollTimer);
            pollTimer = null;
        }
    }

    // --- Resolve channel name to channel ID ---

    async function resolveChannelId(channelName) {
        if (!config.clientId || !config.accessToken) return null;

        try {
            const response = await fetch(
                API_BASE + '/channels?username=' + encodeURIComponent(channelName),
                {
                    headers: {
                        'Authorization': 'Bearer ' + config.accessToken,
                        'client-id': config.clientId,
                        'Accept': 'application/json'
                    }
                }
            );

            if (!response.ok) return null;

            const data = await response.json();
            if (data && data.id) return data.id;
            if (Array.isArray(data) && data.length > 0) return data[0].id;
            return null;
        } catch (err) {
            console.error('Failed to resolve Blaze channel:', err);
            return null;
        }
    }

    // --- C# bridge communication ---

    if (blazeChatHost) {
        blazeChatHost.addEventListener('message', async (event) => {
            const message = event.data;

            switch (message.type) {
                case 'blazeConfig':
                    config.clientId = message.payload.clientId || '';
                    config.accessToken = message.payload.accessToken || '';
                    config.fadeTimeout = message.payload.fadeTimeout || 0;

                    const channelInput = message.payload.channel || '';

                    // If it looks like a UUID, use it directly; otherwise resolve the name
                    if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(channelInput)) {
                        config.channelId = channelInput;
                    } else if (channelInput) {
                        showStatus('Connecting to ' + channelInput + '...', 3000);
                        const resolved = await resolveChannelId(channelInput);
                        if (resolved) {
                            config.channelId = resolved;
                        } else {
                            showStatus('Could not find Blaze channel: ' + channelInput, 5000);
                            return;
                        }
                    }

                    if (config.channelId) {
                        showStatus('Connected to Blaze chat', 3000);
                        startPolling();
                    }
                    break;

                default:
                    console.warn('Unknown message type:', message.type);
            }
        });

        // Notify the C# host that we're ready
        blazeChatHost.postMessage({
            type: 'BlazeChatReady',
            protocolVersion: 1
        });
    } else {
        console.warn('Blaze Chat host bridge is unavailable.');
        showStatus('No host bridge - running standalone', 0);
    }
})();
