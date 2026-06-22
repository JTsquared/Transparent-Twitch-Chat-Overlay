// SUCO - Stream Unified Chat Overlay Client
// Connects to Blaze, Twitch, and Kick chat simultaneously.
// Renders all messages in a single unified view with platform badges.

(function () {
    'use strict';

    var MAX_MESSAGES = 50;
    var API_BASE = 'https://api.blaze.stream/v1';
    var KICK_PUSHER_KEY = '32cbd69e4b950bf97679';
    var KICK_PUSHER_URL = 'wss://ws-us2.pusher.com/app/' + KICK_PUSHER_KEY + '?protocol=7&client=js&version=8.4.0-rc2&flash=false';

    var config = {
        channelId: '', clientId: '', accessToken: '',
        fadeTimeout: 0, textColor: '#ffffff', bgEnabled: false, bgOpacity: 50
    };

    var blazeSocket = null;
    var blazeSessionId = null;
    var twitchSocket = null;
    var kickSocket = null;
    var isConfiguring = false;
    var blazeChatHost = window.chrome && window.chrome.webview ? window.chrome.webview : null;

    // --- Status ---
    function showStatus(text, dur) {
        var el = document.getElementById('status_text');
        el.textContent = text; el.classList.add('visible');
        if (dur > 0) setTimeout(function () { el.classList.remove('visible'); }, dur);
    }

    // --- Rendering ---
    function escapeHtml(s) { var d = document.createElement('div'); d.appendChild(document.createTextNode(s)); return d.innerHTML; }

    function getUserColor(name) {
        var h = 0; for (var i = 0; i < name.length; i++) h = name.charCodeAt(i) + ((h << 5) - h);
        return 'hsl(' + (Math.abs(h) % 360) + ', 70%, 65%)';
    }

    function addMessage(platform, displayName, text, nameColor, messageId) {
        var container = document.getElementById('chat_container');
        var color = nameColor || getUserColor(displayName);
        var textColor = config.textColor || '#ffffff';

        var line = document.createElement('div');
        line.className = 'chat-line' + (config.bgEnabled ? ' no-shadow' : '');
        if (messageId) line.dataset.messageId = messageId;
        line.innerHTML =
            '<span class="platform-badge platform-' + platform + '">' + platform.toUpperCase() + '</span>' +
            '<span class="username" style="color:' + color + '">' + escapeHtml(displayName) + '</span>' +
            '<span class="separator">: </span>' +
            '<span class="message-text" style="color:' + textColor + '">' + escapeHtml(text) + '</span>';

        container.appendChild(line);
        while (container.children.length > MAX_MESSAGES) container.removeChild(container.firstChild);
        container.scrollTop = container.scrollHeight;

        if (config.fadeTimeout > 0) {
            setTimeout(function () {
                line.classList.add('fading');
                setTimeout(function () { if (line.parentNode) line.parentNode.removeChild(line); }, 1000);
            }, config.fadeTimeout * 1000);
        }
    }

    function applyAppearance(p) {
        var c = document.getElementById('chat_container');
        c.style.fontSize = (p.textSize || 18) + 'px';
        c.style.fontFamily = "'" + (p.fontFamily || 'Noto Sans') + "', sans-serif";
        config.textColor = p.textColor || '#ffffff';
        config.bgEnabled = !!p.bgEnabled;
        config.bgOpacity = p.bgOpacity || 50;
        config.fadeTimeout = p.fadeTimeout || 0;

        if (config.bgEnabled) {
            document.body.style.background = 'rgba(0,0,0,' + (config.bgOpacity / 100) + ')';
        } else {
            document.body.style.background = 'transparent';
        }
    }

    // ===================== BLAZE =====================
    async function blazeSubscribe(type, channelId) {
        try {
            var r = await fetch(API_BASE + '/events/subscriptions', {
                method: 'POST',
                headers: { 'Authorization': 'Bearer ' + config.accessToken, 'Client-Id': config.clientId, 'Content-Type': 'application/json', 'Accept': 'application/json' },
                body: JSON.stringify({ type: type, version: '1', sessionId: blazeSessionId, condition: { channelId: channelId } })
            });
            return r.ok;
        } catch (e) { return false; }
    }

    function connectBlaze(channelId) {
        if (blazeSocket) { blazeSocket.disconnect(); blazeSocket = null; }
        blazeSessionId = null;
        console.log('[Blaze] Connecting...');

        blazeSocket = io('https://blaze.stream', {
            path: '/ws', transports: ['websocket'], upgrade: false,
            auth: { token: 'Bearer ' + config.accessToken },
            reconnection: true, reconnectionAttempts: 10, reconnectionDelay: 1000
        });

        blazeSocket.on('eventsub', async function (data) {
            if (!data || !data.metadata) return;
            if (data.metadata.messageType === 'session_welcome') {
                blazeSessionId = data.payload ? data.payload.sessionId : null;
                if (blazeSessionId) {
                    await blazeSubscribe('channel.chat.message', channelId);
                    console.log('[Blaze] Subscribed to chat');
                }
            } else if (data.metadata.messageType === 'notification') {
                var p = data.payload || {};
                if (data.metadata.subscriptionType === 'channel.chat.message' && p.sender && p.message) {
                    addMessage('blaze', p.sender.displayName || p.sender.username || 'Anon', p.message, null, p.messageId);
                }
            }
        });

        blazeSocket.on('connect_error', function (e) { console.error('[Blaze] Error:', e.message); });
    }

    async function resolveBlaze(slug) {
        try {
            var r = await fetch(API_BASE + '/channels?slug[]=' + encodeURIComponent(slug), {
                headers: { 'Authorization': 'Bearer ' + config.accessToken, 'Client-Id': config.clientId, 'Accept': 'application/json' }
            });
            if (!r.ok) return null;
            var d = await r.json();
            return (d.data && d.data.rows && d.data.rows.length > 0) ? d.data.rows[0].id : null;
        } catch (e) { return null; }
    }

    // ===================== TWITCH =====================
    function connectTwitch(channel) {
        channel = channel.toLowerCase().replace('#', '');
        if (twitchSocket) { try { twitchSocket.close(); } catch(e){} twitchSocket = null; }
        console.log('[Twitch] Connecting to #' + channel);

        var nick = 'justinfan' + Math.floor(Math.random() * 99999);
        twitchSocket = new WebSocket('wss://irc-ws.chat.twitch.tv:443');

        twitchSocket.onopen = function () {
            twitchSocket.send('NICK ' + nick);
            twitchSocket.send('CAP REQ :twitch.tv/tags');
            twitchSocket.send('JOIN #' + channel);
            console.log('[Twitch] Joined #' + channel);
        };

        twitchSocket.onmessage = function (event) {
            var lines = event.data.split('\r\n');
            for (var i = 0; i < lines.length; i++) {
                var line = lines[i];
                if (!line) continue;
                if (line.startsWith('PING')) { twitchSocket.send('PONG' + line.substring(4)); continue; }
                if (line.indexOf('PRIVMSG') === -1) continue;

                var tags = {};
                var rest = line;
                if (rest.startsWith('@')) {
                    var tagEnd = rest.indexOf(' ');
                    rest.substring(1, tagEnd).split(';').forEach(function (t) { var kv = t.split('='); tags[kv[0]] = kv[1] || ''; });
                    rest = rest.substring(tagEnd + 1);
                }

                var msgIdx = rest.indexOf(' PRIVMSG ');
                if (msgIdx === -1) continue;
                var afterPrivmsg = rest.substring(msgIdx + 9);
                var textIdx = afterPrivmsg.indexOf(' :');
                if (textIdx === -1) continue;

                addMessage('twitch', tags['display-name'] || 'Anonymous', afterPrivmsg.substring(textIdx + 2), tags['color'] || null, tags['id']);
            }
        };

        twitchSocket.onclose = function () {
            console.log('[Twitch] Disconnected, reconnecting in 5s...');
            setTimeout(function () { connectTwitch(channel); }, 5000);
        };
    }

    // ===================== KICK =====================
    async function resolveKickChatroom(username) {
        try {
            var r = await fetch('https://kick.com/api/v2/channels/' + encodeURIComponent(username));
            if (!r.ok) return null;
            var d = await r.json();
            return d.chatroom ? d.chatroom.id : null;
        } catch (e) { return null; }
    }

    function connectKick(chatroomId) {
        if (kickSocket) { try { kickSocket.close(); } catch(e){} kickSocket = null; }
        console.log('[Kick] Connecting to chatroom ' + chatroomId);

        kickSocket = new WebSocket(KICK_PUSHER_URL);

        kickSocket.onmessage = function (event) {
            try {
                var msg = JSON.parse(event.data);
                if (msg.event === 'pusher:connection_established') {
                    kickSocket.send(JSON.stringify({ event: 'pusher:subscribe', data: { channel: 'chatrooms.' + chatroomId } }));
                    console.log('[Kick] Subscribed');
                    return;
                }
                if (msg.event === 'App\\Events\\ChatMessageEvent' || msg.event === 'App\\Events\\ChatMessageSentEvent') {
                    var data = typeof msg.data === 'string' ? JSON.parse(msg.data) : msg.data;
                    var username = (data.sender && data.sender.username) || (data.user && data.user.username) || 'Anon';
                    var text = data.content || data.message || '';
                    if (text) addMessage('kick', username, text, null, data.id);
                }
            } catch (e) {}
        };

        kickSocket.onclose = function () {
            console.log('[Kick] Disconnected, reconnecting in 5s...');
            setTimeout(function () { connectKick(chatroomId); }, 5000);
        };
    }

    // ===================== C# BRIDGE =====================
    console.log('[SUCO] Script loaded, host bridge:', blazeChatHost ? 'available' : 'unavailable');

    if (blazeChatHost) {
        blazeChatHost.addEventListener('message', function (event) {
            var message = event.data;
            if (message.type !== 'blazeConfig') return;
            if (isConfiguring) return;
            isConfiguring = true;

            var p = message.payload;
            config.clientId = p.clientId || '';
            config.accessToken = p.accessToken || '';
            applyAppearance(p);

            console.log('[SUCO] Config received');

            (async function () {
                try {
                    var connected = [];

                    // Blaze
                    var blazeCh = p.channel || '';
                    if (blazeCh && config.accessToken) {
                        var blazeId = await resolveBlaze(blazeCh);
                        if (blazeId) { connectBlaze(blazeId); connected.push('Blaze'); }
                        else console.error('[Blaze] Could not resolve:', blazeCh);
                    }

                    // Twitch
                    var twitchCh = p.twitchChannel || '';
                    if (twitchCh) { connectTwitch(twitchCh); connected.push('Twitch'); }

                    // Kick
                    var kickCh = p.kickChannel || '';
                    if (kickCh) {
                        var kickRoom = await resolveKickChatroom(kickCh);
                        if (kickRoom) { connectKick(kickRoom); connected.push('Kick'); }
                        else console.error('[Kick] Could not resolve:', kickCh);
                    }

                    if (connected.length > 0) showStatus('Connected: ' + connected.join(', '), 3000);
                    else showStatus('No platforms configured', 5000);
                } catch (err) {
                    console.error('[SUCO] Error:', err);
                    showStatus('Error: ' + err.message, 5000);
                }
                isConfiguring = false;
            })();
        });

        blazeChatHost.postMessage({ type: 'BlazeChatReady', protocolVersion: 1 });
    }
})();
