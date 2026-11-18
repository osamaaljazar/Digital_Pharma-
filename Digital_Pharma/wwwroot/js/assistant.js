(() => {
    const token = document.querySelector('meta[name="csrf-token"]')?.content || '';

    function addMessage(container, role, text, extraClass = '') {
        const message = document.createElement('div');
        message.className = `dp-chat-message ${role} ${extraClass}`.trim();
        message.textContent = text;
        container.appendChild(message);
        container.scrollTop = container.scrollHeight;
        return message;
    }

    function conversationText(messages) {
        return Array.from(messages.querySelectorAll('.dp-chat-message:not(.loading)'))
            .slice(-6)
            .map(item => `${item.classList.contains('user') ? 'المستخدم' : 'المساعد'}: ${item.textContent || ''}`)
            .join('\n')
            .slice(-2400);
    }

    function saveGlobalConversation(root) {
        if (!root.classList.contains('dp-assistant')) return;
        const items = Array.from(root.querySelectorAll('.dp-chat-message:not(.loading)'))
            .slice(-10)
            .map(item => ({ role: item.classList.contains('user') ? 'user' : 'ai', text: (item.textContent || '').slice(0, 1200) }));
        try { sessionStorage.setItem('digital-pharma-ai-chat-v1', JSON.stringify(items)); } catch { }
    }

    function restoreGlobalConversation(root) {
        if (!root.classList.contains('dp-assistant')) return;
        try {
            const items = JSON.parse(sessionStorage.getItem('digital-pharma-ai-chat-v1') || '[]');
            if (!Array.isArray(items) || items.length === 0) return;
            const messages = root.querySelector('.dp-chat-messages');
            messages.replaceChildren();
            items.slice(-10).forEach(item => {
                if ((item?.role === 'user' || item?.role === 'ai') && typeof item.text === 'string')
                    addMessage(messages, item.role, item.text.slice(0, 1200));
            });
        } catch { sessionStorage.removeItem('digital-pharma-ai-chat-v1'); }
    }

    async function submitChat(root, question) {
        const messages = root.querySelector('.dp-chat-messages');
        const submit = root.querySelector('.dp-chat-form button');
        const history = conversationText(messages);
        addMessage(messages, 'user', question);
        const loading = addMessage(messages, 'ai', 'جارٍ إعداد الإجابة…', 'loading');
        submit.disabled = true;

        const payload = {
            question,
            scope: root.dataset.aiChat === 'drug' ? 'drug' : 'global',
            drugName: root.dataset.drugName || document.querySelector('[data-ai-chat="drug"]')?.dataset.drugName || null,
            pageKey: root.dataset.pageKey || document.querySelector('.dp-assistant')?.dataset.pageKey || '',
            conversation: history,
            includeHealth: root.querySelector('.dp-health-consent input')?.checked === true
        };

        try {
            const response = await fetch('/Home/AssistantChat', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
                body: JSON.stringify(payload)
            });
            const data = await response.json().catch(() => null);
            loading.remove();
            addMessage(messages, 'ai', data?.answer || (response.status === 429 ? 'تم بلوغ حد الأسئلة مؤقتاً. حاول بعد دقيقة.' : 'تعذرت الإجابة حالياً.'));
            saveGlobalConversation(root);
        } catch {
            loading.remove();
            addMessage(messages, 'ai', 'تعذر الاتصال بالمساعد. تحقق من الاتصال وحاول مجدداً.');
            saveGlobalConversation(root);
        } finally {
            submit.disabled = false;
        }
    }

    function initializeChat(root) {
        const form = root.querySelector('.dp-chat-form');
        const input = form?.querySelector('textarea');
        if (!form || !input) return;
        root.querySelectorAll('.dp-chat-suggestions button').forEach(button => button.addEventListener('click', () => {
            input.value = button.textContent || '';
            input.focus();
        }));
        input.addEventListener('input', () => {
            input.style.height = 'auto';
            input.style.height = `${Math.min(input.scrollHeight, 110)}px`;
        });
        input.addEventListener('keydown', event => {
            if (event.key === 'Enter' && !event.shiftKey) {
                event.preventDefault();
                form.requestSubmit();
            }
        });
        form.addEventListener('submit', async event => {
            event.preventDefault();
            const question = input.value.trim();
            if (question.length < 2) return;
            input.value = '';
            input.style.height = 'auto';
            await submitChat(root, question);
            input.focus();
        });
    }

    document.querySelectorAll('.dp-embedded-chat').forEach(initializeChat);
    const assistant = document.querySelector('.dp-assistant');
    if (assistant) {
        assistant.dataset.aiChat = 'global';
        restoreGlobalConversation(assistant);
        initializeChat(assistant);
        const launcher = assistant.querySelector('.dp-assistant-launcher');
        const panel = assistant.querySelector('.dp-assistant-panel');
        const close = assistant.querySelector('.dp-assistant-close');
        const setOpen = open => {
            panel.hidden = !open;
            launcher.setAttribute('aria-expanded', String(open));
            if (innerWidth <= 640) document.body.classList.toggle('dp-assistant-open', open);
            if (open) panel.querySelector('textarea')?.focus();
        };
        launcher.addEventListener('click', () => setOpen(panel.hidden));
        close.addEventListener('click', () => setOpen(false));
        document.addEventListener('keydown', event => { if (event.key === 'Escape' && !panel.hidden) setOpen(false); });
    }
})();
