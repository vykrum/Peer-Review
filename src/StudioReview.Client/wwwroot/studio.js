// Studio Peer Review WASM Client Helper
window.studioReview = {
    getReviewerInfo: function() {
        var token = localStorage.getItem("studio_reviewer_token");
        var nick = localStorage.getItem("studio_reviewer_nick");

        if (!token) {
            token = "rev_" + Math.random().toString(36).substring(2, 10) + "_" + Date.now().toString(36);
            localStorage.setItem("studio_reviewer_token", token);
        }

        if (!nick) {
            var prefixes = [
                "Tectonic", "Cantilever", "Bauhaus", "Brutalist", "Parametric",
                "Monolithic", "Biophilic", "Axonometric", "Diagrid", "Sectional",
                "Tension", "Folded-Plate", "Spatial", "Stereotomic", "Climatic"
            ];
            var nouns = [
                "Atrium", "Vault", "Facade", "Canopy", "Truss",
                "Pavilion", "Loggia", "Arcade", "Peristyle", "Spire",
                "Piloti", "Brise-Soleil", "Courtyard", "Portico", "Gallery"
            ];
            var p = prefixes[Math.floor(Math.random() * prefixes.length)];
            var n = nouns[Math.floor(Math.random() * nouns.length)];
            var num = Math.floor(Math.random() * 89) + 11;
            nick = p + " " + n + " #" + num;
            localStorage.setItem("studio_reviewer_nick", nick);
        }

        return JSON.stringify({ token: token, nickname: nick });
    },

    saveReviewerNickname: function(nick) {
        if (nick && nick.trim()) {
            localStorage.setItem("studio_reviewer_nick", nick.trim());
        }
    },

    getLocalStorage: function(key) {
        return localStorage.getItem(key) || "";
    },

    setLocalStorage: function(key, val) {
        localStorage.setItem(key, val);
    },

    downloadTextFile: function(filename, content, mimeType) {
        try {
            var blob = new Blob([content], { type: mimeType || 'text/plain;charset=utf-8' });
            var url = URL.createObjectURL(blob);
            var a = document.createElement('a');
            a.href = url;
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            setTimeout(function() { URL.revokeObjectURL(url); }, 1000);
        } catch (e) {
            console.error("Download failed", e);
        }
    },

    copyToClipboard: function(text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text);
        }
    },

    scrollToTop: function() {
        window.scrollTo({ top: 0, behavior: 'smooth' });
    }
};
