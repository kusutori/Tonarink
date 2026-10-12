// Bootstrap text is available before the .NET renderer connects. The shared
// layout calls setLanguage again with its persisted language after rendering.
(() => {
    const messages = {
    "en-US": {
        "loading": "Loading…",
        "error": "An unexpected error occurred",
        "reload": "Reload",
        "dismiss": "Dismiss",
        "description": "Tonarink — an unofficial LocalSend-compatible file sharing client."
    },
    "zh-CN": {
        "loading": "正在加载…",
        "error": "发生了意外错误",
        "reload": "重新加载",
        "dismiss": "关闭",
        "description": "Tonarink — 兼容 LocalSend 的非官方文件分享客户端"
    },
    "ja-JP": {
        "loading": "読み込み中…",
        "error": "予期しないエラーが発生しました",
        "reload": "再読み込み",
        "dismiss": "閉じる",
        "description": "Tonarink — LocalSend 対応の非公式ファイル共有クライアント"
    },
    "ko-KR": {
        "loading": "불러오는 중…",
        "error": "예기치 않은 오류가 발생했습니다",
        "reload": "새로 고침",
        "dismiss": "닫기",
        "description": "Tonarink — LocalSend 호환 비공식 파일 공유 클라이언트"
    },
    "fr-FR": {
        "loading": "Chargement…",
        "error": "Une erreur inattendue s’est produite",
        "reload": "Recharger",
        "dismiss": "Fermer",
        "description": "Tonarink — un client non officiel de partage de fichiers compatible LocalSend"
    },
    "es-ES": {
        "loading": "Cargando…",
        "error": "Se produjo un error inesperado",
        "reload": "Recargar",
        "dismiss": "Cerrar",
        "description": "Tonarink — un cliente no oficial para compartir archivos compatible con LocalSend"
    }
};
    const match = locale => Object.keys(messages).find(candidate =>
        candidate.split('-')[0] === String(locale || '').toLowerCase().split('-')[0]) || 'en-US';
    window.tonarink ??= {};
    window.tonarink.setLanguage = locale => {
        const selected = match(locale);
        const text = messages[selected];
        document.documentElement.lang = selected;
        document.querySelectorAll('[data-tonarink-text]').forEach(element => {
            const value = text[element.dataset.tonarinkText];
            if (value !== undefined) element.textContent = value;
        });
        document.querySelectorAll('[data-tonarink-label]').forEach(element => {
            const value = text[element.dataset.tonarinkLabel];
            if (value !== undefined) element.setAttribute('aria-label', value);
        });
        const description = document.querySelector('meta[name="description"]');
        if (description) description.content = text.description;
    };
    window.tonarink.setLanguage(navigator.language);
})();
