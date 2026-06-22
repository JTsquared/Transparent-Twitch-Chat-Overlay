// Blaze Chat Overlay Client
// Connects to Blaze Stream EventSub via Socket.IO for real-time chat messages.
// Modeled after the working BlazeEventSub.js from blaze_games.

(function () {
    'use strict';

    const MAX_MESSAGES = 50;
    const API_BASE = 'https://api.blaze.stream/v1';
    const SOCKET_URL = 'https://blaze.stream';
    const SOCKET_PATH = '/ws';

    let config = {
        channelId: '',
        clientId: '',
        accessToken: '',
        fadeTimeout: 0
    };

    let socket = null;
    let sessionId = null;
    const blazeChatHost = window.chrome && window.chrome.webview ? window.chrome.webview : null;

    // --- Status display ---

    function showStatus(text, duration) {
        const el = document.getElementById('status_text');
        el.textContent = text;
        el.classList.add('visible');
        if (duration > 0) {
            setTimeout(function () { el.classList.remove('visible'); }, duration);
        }
    }

    // --- Chat rendering ---

    function escapeHtml(str) {
        const div = document.createElement('div');
        div.appendChild(document.createTextNode(str));
        return div.innerHTML;
    }

    function getUserColor(username) {
        var hash = 0;
        for (var i = 0; i < username.length; i++) {
            hash = username.charCodeAt(i) + ((hash << 5) - hash);
        }
        return 'hsl(' + (Math.abs(hash) % 360) + ', 70%, 65%)';
    }

    function renderBadges(roles) {
        var html = '';
        if (!roles || !Array.isArray(roles)) return html;
        if (roles.includes('moderator')) html += '<span class="badge badge-mod" title="Moderator">M</span>';
        if (roles.includes('vip'))       html += '<span class="badge badge-vip" title="VIP">V</span>';
        if (roles.includes('og'))        html += '<span class="badge badge-og" title="OG">OG</span>';
        if (roles.includes('subscriber'))html += '<span class="badge badge-sub" title="Subscriber">S</span>';
        return html;
    }

    function addChatMessage(payload) {
        var container = document.getElementById('chat_container');
        var sender = payload.sender || {};
        var displayName = sender.displayName || sender.username || 'Anonymous';
        var color = getUserColor(displayName);
        var badges = renderBadges(sender.roles);
        var text = escapeHtml(payload.message || '');

        var line = document.createElement('div');
        line.className = 'chat-line';
        line.dataset.messageId = payload.messageId || '';
        line.innerHTML =
            badges +
            '<span class="username" style="color:' + color + '">' + escapeHtml(displayName) + '</span>' +
            '<span class="separator">: </span>' +
            '<span class="message-text">' + text + '</span>';

        container.appendChild(line);

        while (container.children.length > MAX_MESSAGES) {
            container.removeChild(container.firstChild);
        }
        container.scrollTop = container.scrollHeight;

        if (config.fadeTimeout > 0) {
            setTimeout(function () {
                line.classList.add('fading');
                setTimeout(function () {
                    if (line.parentNode) line.parentNode.removeChild(line);
                }, 1000);
            }, config.fadeTimeout * 1000);
        }
    }

    function handleMessageDelete(payload) {
        var el = payload.messageId ? document.querySelector('[data-message-id="' + payload.messageId + '"]') : null;
        if (el) {
            el.classList.add('fading');
            setTimeout(function () { if (el.parentNode) el.parentNode.removeChild(el); }, 500);
        }
    }

    function handleChatClear() {
        document.getElementById('chat_container').innerHTML = '';
    }

    // --- Resolve channel slug to channel UUID ---

    async function resolveChannelId(channelName) {
        if (!config.clientId || !config.accessToken) return null;

        try {
            var url = API_BASE + '/channels?slug[]=' + encodeURIComponent(channelName);
            console.log('[BlazeChat] Resolving channel via:', url);

            var response = await fetch(url, {
                headers: {
                    'Authorization': 'Bearer ' + config.accessToken,
                    'Client-Id': config.clientId,
                    'Accept': 'application/json'
                }
            });

            console.log('[BlazeChat] Channel lookup status:', response.status);

            if (!response.ok) {
                var errText = await response.text();
                console.error('[BlazeChat] Channel lookup failed:', response.status, errText);
                return null;
            }

            var data = await response.json();
            console.log('[BlazeChat] Channel lookup result:', JSON.stringify(data).substring(0, 500));

            // Blaze API returns { success, data: { count, rows: [...] } }
            if (data.data && data.data.rows && data.data.rows.length > 0) return data.data.rows[0].id;
            // Fallback: try other shapes
            var channels = data.channels || data.data || data;
            if (Array.isArray(channels) && channels.length > 0) return channels[0].id;
            if (data && data.id) return data.id;
            return null;
        } catch (err) {
            console.error('[BlazeChat] Failed to resolve channel:', err);
            return null;
        }
    }

    // --- Socket.IO EventSub (matches working BlazeEventSub.js pattern) ---

    async function subscribeToEvent(type, channelId) {
        try {
            console.log('[BlazeChat] Subscribing to', type, 'for channel', channelId);
            var response = await fetch(API_BASE + '/events/subscriptions', {
                method: 'POST',
                headers: {
                    'Authorization': 'Bearer ' + config.accessToken,
                    'Client-Id': config.clientId,
                    'Content-Type': 'application/json',
                    'Accept': 'application/json'
                },
                body: JSON.stringify({
                    type: type,
                    version: '1',
                    sessionId: sessionId,
                    condition: { channelId: channelId }
                })
            });

            if (!response.ok) {
                var errBody = await response.text();
                console.error('[BlazeChat] Subscribe failed for', type, '(' + response.status + '):', errBody.substring(0, 200));
                return false;
            }

            console.log('[BlazeChat] Subscribed to', type);
            return true;
        } catch (err) {
            console.error('[BlazeChat] Subscribe error for', type, ':', err);
            return false;
        }
    }

    function connectSocket() {
        if (socket) {
            socket.disconnect();
            socket = null;
        }

        sessionId = null;
        showStatus('Connecting to Blaze chat...', 0);
        console.log('[BlazeChat] Connecting Socket.IO to', SOCKET_URL, 'path:', SOCKET_PATH);

        // Match the working BlazeEventSub.js connection pattern exactly
        socket = io(SOCKET_URL, {
            path: SOCKET_PATH,
            transports: ['websocket'],
            upgrade: false,
            auth: {
                token: 'Bearer ' + config.accessToken
            },
            reconnection: true,
            reconnectionAttempts: 10,
            reconnectionDelay: 1000,
            reconnectionDelayMax: 30000
        });

        socket.on('connect', function () {
            console.log('[BlazeChat] Socket.IO connected, waiting for session_welcome...');
        });

        socket.on('disconnect', function (reason) {
            console.log('[BlazeChat] Socket.IO disconnected:', reason);
            showStatus('Disconnected. Reconnecting...', 5000);
            sessionId = null;
        });

        socket.on('connect_error', function (err) {
            console.error('[BlazeChat] Socket.IO connection error:', err.message);
            showStatus('Connection error: ' + err.message, 5000);
        });

        // Listen for EventSub messages
        socket.on('eventsub', async function (data) {
            if (!data || !data.metadata) return;

            var messageType = data.metadata.messageType;

            if (messageType === 'session_welcome') {
                // Match working code: payload.sessionId (not payload.session.id)
                sessionId = data.payload ? data.payload.sessionId : null;

                if (!sessionId) {
                    console.error('[BlazeChat] No sessionId in welcome:', JSON.stringify(data));
                    showStatus('Failed to get session ID', 5000);
                    return;
                }

                console.log('[BlazeChat] Session established:', sessionId);

                // Subscribe to chat events
                var chatOk = await subscribeToEvent('channel.chat.message', config.channelId);
                await subscribeToEvent('channel.chat.message_delete', config.channelId);
                await subscribeToEvent('channel.chat.clear', config.channelId);

                if (chatOk) {
                    showStatus('Connected to Blaze chat', 3000);
                } else {
                    showStatus('Failed to subscribe to chat events', 5000);
                }
            }
            else if (messageType === 'notification') {
                var subType = data.metadata.subscriptionType;
                var payload = data.payload || {};

                if (subType === 'channel.chat.message') {
                    addChatMessage(payload);
                } else if (subType === 'channel.chat.message_delete') {
                    handleMessageDelete(payload);
                } else if (subType === 'channel.chat.clear') {
                    handleChatClear();
                }
            }
        });
    }

    // --- C# bridge communication ---

    console.log('[BlazeChat] Script loaded, host bridge:', blazeChatHost ? 'available' : 'unavailable');

    if (blazeChatHost) {
        blazeChatHost.addEventListener('message', async function (event) {
            var message = event.data;
            console.log('[BlazeChat] Received config from C#');

            if (message.type !== 'blazeConfig') {
                console.warn('[BlazeChat] Unknown message type:', message.type);
                return;
            }

            config.clientId = message.payload.clientId || '';
            config.accessToken = message.payload.accessToken || '';
            config.fadeTimeout = message.payload.fadeTimeout || 0;

            console.log('[BlazeChat] clientId:', config.clientId ? 'present' : 'MISSING',
                'token:', config.accessToken ? 'present' : 'MISSING');

            var channelInput = message.payload.channel || '';

            // If it looks like a UUID, use directly; otherwise resolve slug to UUID
            if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(channelInput)) {
                config.channelId = channelInput;
                console.log('[BlazeChat] Channel is UUID:', config.channelId);
            } else if (channelInput) {
                showStatus('Resolving channel: ' + channelInput + '...', 0);
                console.log('[BlazeChat] Resolving slug:', channelInput);
                var resolved = await resolveChannelId(channelInput);
                if (resolved) {
                    config.channelId = resolved;
                    console.log('[BlazeChat] Resolved to:', config.channelId);
                } else {
                    showStatus('Could not find channel: ' + channelInput, 5000);
                    console.error('[BlazeChat] Channel resolution failed for:', channelInput);
                    return;
                }
            }

            if (config.channelId && config.accessToken) {
                console.log('[BlazeChat] All ready, connecting Socket.IO...');
                connectSocket();
            } else {
                console.error('[BlazeChat] Missing -',
                    !config.channelId ? 'channelId' : '',
                    !config.accessToken ? 'accessToken' : '');
                showStatus('Missing channel or credentials', 5000);
            }
        });

        // Notify C# host that we're ready to receive config
        blazeChatHost.postMessage({
            type: 'BlazeChatReady',
            protocolVersion: 1
        });
    } else {
        console.warn('[BlazeChat] Host bridge unavailable (standalone mode)');
        showStatus('No host bridge', 0);
    }
})();
