// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System.Collections.Generic;
using Sillet.App.Services;

namespace Sillet.Mobile
{
    public static class MobileStrings
    {
        private static bool _done;

        private static readonly Dictionary<string, string[]> Extra = new Dictionary<string, string[]>
        {
            { "Nothing new", new[] { "Nada nuevo", "Ничего нового", "暂无新内容", "Rien de nouveau" } },
            { "Want to share this screenshot?", new[] { "¿Quieres compartir esta captura?", "Поделиться этим скриншотом?", "要分享这张截图吗？", "Partager cette capture d'écran ?" } },
            { "Check that it shows nothing private before sending it", new[] { "Revisa que no muestre nada privado antes de enviarla", "Проверьте, что на нём нет ничего личного", "发送前请确认其中没有隐私信息", "Vérifiez qu'elle ne montre rien de privé avant de l'envoyer" } },
            { "Not now", new[] { "Ahora no", "Не сейчас", "以后再说", "Pas maintenant" } },
            { "Share", new[] { "Compartir", "Поделиться", "分享", "Partager" } },
            { "Watch-only. Balances and receive addresses, no keys on this phone", new[] { "Solo ver. Saldos y direcciones, sin llaves en este teléfono", "Только просмотр. Балансы и адреса, без ключей на телефоне", "仅查看。余额和收款地址，本机没有私钥", "Lecture seule. Soldes et adresses de réception, aucune clé sur ce téléphone" } },
            { "Turn off to share screenshots of Sillet", new[] { "Desactívalo para compartir capturas de Sillet", "Выключите, чтобы делиться скриншотами Sillet", "关闭后即可分享 Sillet 截图", "Désactivez pour partager des captures d'écran de Sillet" } },
            { "Danger zone", new[] { "Zona de peligro", "Опасная зона", "危险区域", "Zone dangereuse" } },
            { "Share address", new[] { "Compartir dirección", "Поделиться адресом", "分享地址", "Partager l'adresse" } },
            { "Share QR", new[] { "Compartir QR", "Поделиться QR", "分享二维码", "Partager le QR" } },
            { "Tap Receive to get your addresses", new[] { "Toca Recibir para ver tus direcciones", "Нажмите «Получить», чтобы увидеть адреса", "点击“接收”获取你的地址", "Touchez Recevoir pour obtenir vos adresses" } },
            { "Coming soon", new[] { "Muy pronto", "Скоро", "即将推出", "Bientôt disponible" } },
            { "This part is available on the desktop app for now", new[] { "Por ahora esta parte está en la app de escritorio", "Пока это доступно только в приложении для ПК", "此功能目前仅在桌面版提供", "Cette partie n'est disponible que sur l'application de bureau pour le moment" } },
            { "24h", new[] { "24h", "24ч", "24小时", "24 h" } },
            { "Paste the receive addresses from Ledger Live, Trezor Suite or your device app. Sillet shows balances; sending is done from the device app", new[] { "Pega las direcciones de recepción de Ledger Live, Trezor Suite o la app de tu dispositivo. Sillet muestra los saldos; los envíos se hacen desde la app del dispositivo", "Вставьте адреса получения из Ledger Live, Trezor Suite или приложения устройства. Sillet показывает балансы, а отправка делается в приложении устройства", "粘贴 Ledger Live、Trezor Suite 或设备应用中的收款地址。Sillet 显示余额，发送需在设备应用中完成", "Collez les adresses de réception depuis Ledger Live, Trezor Suite ou l'application de votre appareil. Sillet affiche les soldes ; les envois se font depuis l'application de l'appareil" } },
            { "Protects the wallet on this phone. At least 8 characters", new[] { "Protege la wallet en este teléfono. Al menos 8 caracteres", "Защищает кошелёк на этом телефоне. Не менее 8 символов", "保护本手机上的钱包。至少 8 个字符", "Protège le portefeuille sur ce téléphone. Au moins 8 caractères" } },
            { "Finish", new[] { "Terminar", "Готово", "完成", "Terminer" } },
            { "Swipe to send", new[] { "Desliza para enviar", "Проведите, чтобы отправить", "滑动发送", "Glissez pour envoyer" } },
            { "Sending...", new[] { "Enviando...", "Отправка...", "发送中...", "Envoi en cours..." } },
            { "Fingerprint unlock is coming soon to the mobile app", new[] { "El desbloqueo con huella llegará pronto a la app móvil", "Разблокировка отпечатком скоро появится в мобильном приложении", "指纹解锁即将登陆移动版", "Le déverrouillage par empreinte arrive bientôt sur l'application mobile" } },
            { "Phone notifications while Sillet is in the background", new[] { "Notificaciones en el teléfono mientras Sillet está en segundo plano", "Уведомления на телефоне, пока Sillet в фоне", "Sillet 在后台时显示手机通知", "Notifications sur le téléphone quand Sillet est en arrière-plan" } },
            { "Sillet alerts", new[] { "Alertas de Sillet", "Оповещения Sillet", "Sillet 提醒", "Alertes Sillet" } },
            { "Payments, news and price moves", new[] { "Pagos, noticias y movimientos de precio", "Платежи, новости и изменения цен", "付款、新闻和价格波动", "Paiements, actualités et variations de prix" } },
            { "Biometric unlock is ready", new[] { "Desbloqueo biométrico listo", "Биометрическая разблокировка готова", "生物识别解锁已就绪", "Déverrouillage biométrique prêt" } },
            { "Biometric unlock removed", new[] { "Desbloqueo biométrico quitado", "Биометрическая разблокировка удалена", "已移除生物识别解锁", "Déverrouillage biométrique supprimé" } },
            { "Password changed. Set up biometric unlock again", new[] { "Contraseña cambiada. Configura el desbloqueo biométrico de nuevo", "Пароль изменён. Настройте биометрию заново", "密码已更改。请重新设置生物识别解锁", "Mot de passe modifié. Configurez à nouveau le déverrouillage biométrique" } },
            { "Your password changed since biometric unlock was set up. Unlock with your password and set it up again", new[] { "Tu contraseña cambió desde que configuraste el desbloqueo biométrico. Desbloquea con tu contraseña y configúralo de nuevo", "Пароль изменился после настройки биометрии. Разблокируйте паролем и настройте заново", "设置生物识别后密码已更改。请用密码解锁并重新设置", "Votre mot de passe a changé depuis la configuration du déverrouillage biométrique. Déverrouillez avec votre mot de passe et configurez-le à nouveau" } },
            { "Biometric unlock is not set up", new[] { "El desbloqueo biométrico no está configurado", "Биометрическая разблокировка не настроена", "未设置生物识别解锁", "Le déverrouillage biométrique n'est pas configuré" } },
            { "The saved biometric data can't be read. Unlock with your password and set it up again", new[] { "No se pueden leer los datos biométricos guardados. Desbloquea con tu contraseña y configúralo de nuevo", "Сохранённые биометрические данные не читаются. Разблокируйте паролем и настройте заново", "无法读取已保存的生物识别数据。请用密码解锁并重新设置", "Les données biométriques enregistrées sont illisibles. Déverrouillez avec votre mot de passe et configurez-les à nouveau" } },
            { "Could not save the biometric data: {0}", new[] { "No se pudieron guardar los datos biométricos: {0}", "Не удалось сохранить биометрические данные: {0}", "无法保存生物识别数据：{0}", "Impossible d'enregistrer les données biométriques : {0}" } },
            { "Set up biometric unlock", new[] { "Configurar desbloqueo biométrico", "Настроить биометрическую разблокировку", "设置生物识别解锁", "Configurer le déverrouillage biométrique" } },
            { "On. Unlock and confirm sends with your fingerprint or face", new[] { "Activo. Desbloquea y confirma envíos con tu huella o cara", "Включено. Разблокируйте и подтверждайте отпечатком или лицом", "已开启。使用指纹或面部解锁和确认发送", "Activé. Déverrouillez et confirmez les envois avec votre empreinte ou votre visage" } },
            { "Unlock with your fingerprint or face instead of typing the password", new[] { "Desbloquea con tu huella o cara en vez de escribir la contraseña", "Разблокируйте отпечатком или лицом вместо ввода пароля", "用指纹或面部解锁，无需输入密码", "Déverrouillez avec votre empreinte ou votre visage au lieu de saisir le mot de passe" } },
            { "Biometric unlock", new[] { "Desbloqueo biométrico", "Биометрическая разблокировка", "生物识别解锁", "Déverrouillage biométrique" } },
            { "Unlock with fingerprint", new[] { "Desbloquear con huella", "Разблокировать отпечатком", "使用指纹解锁", "Déverrouiller avec l'empreinte" } },
            { "Confirm with fingerprint", new[] { "Confirmar con huella", "Подтвердить отпечатком", "使用指纹确认", "Confirmer avec l'empreinte" } },
            { "Biometric unlock needs Android 11 or newer", new[] { "El desbloqueo biométrico necesita Android 11 o superior", "Для биометрии нужен Android 11 или новее", "生物识别解锁需要 Android 11 或更高版本", "Le déverrouillage biométrique nécessite Android 11 ou plus récent" } },
            { "Add a fingerprint or face in your phone settings first", new[] { "Primero agrega una huella o tu cara en los ajustes del teléfono", "Сначала добавьте отпечаток или лицо в настройках телефона", "请先在手机设置中添加指纹或面部", "Ajoutez d'abord une empreinte ou un visage dans les paramètres du téléphone" } },
            { "This phone has no biometric unlock that Sillet can use", new[] { "Este teléfono no tiene un desbloqueo biométrico que Sillet pueda usar", "На этом телефоне нет биометрии, которую может использовать Sillet", "此手机没有 Sillet 可用的生物识别解锁", "Ce téléphone n'a pas de déverrouillage biométrique que Sillet puisse utiliser" } },
            { "Biometric check failed", new[] { "Falló la verificación biométrica", "Проверка биометрии не прошла", "生物识别验证失败", "Échec de la vérification biométrique" } },
            { "Canceled", new[] { "Cancelado", "Отменено", "已取消", "Annulé" } },
            { "Dark", new[] { "Oscuro", "Тёмная", "深色", "Sombre" } },
            { "Light", new[] { "Claro", "Светлая", "浅色", "Clair" } },
            { "Midnight", new[] { "Medianoche", "Полночь", "午夜", "Minuit" } },
            { "Horror", new[] { "Terror", "Хоррор", "恐怖", "Horreur" } }
        };

        private static readonly Dictionary<string, string> Renames = new Dictionary<string, string>
        {
            { "Windows Hello is ready", "Biometric unlock is ready" },
            { "Windows Hello removed", "Biometric unlock removed" },
            { "Password changed. Set up Windows Hello again", "Password changed. Set up biometric unlock again" },
            { "Your password changed since Windows Hello was set up. Unlock with your password and set it up again", "Your password changed since biometric unlock was set up. Unlock with your password and set it up again" },
            { "Windows Hello is not set up", "Biometric unlock is not set up" },
            { "The saved Windows Hello data can't be read. Unlock with your password and set it up again", "The saved biometric data can't be read. Unlock with your password and set it up again" },
            { "Could not save the Windows Hello data: {0}", "Could not save the biometric data: {0}" },
            { "Set up Windows Hello", "Set up biometric unlock" },
            { "On. Unlock and confirm sends with your face, fingerprint or PIN", "On. Unlock and confirm sends with your fingerprint or face" },
            { "Unlock with your face, fingerprint or PIN instead of typing the password", "Unlock with your fingerprint or face instead of typing the password" }
        };

        public static void Register()
        {
            if (_done) return;
            _done = true;
            foreach (var pair in Renames) Loc.Overrides[pair.Key] = pair.Value;
            foreach (var pair in Extra)
                if (!LocTable.Rows.ContainsKey(pair.Key)) LocTable.Rows[pair.Key] = pair.Value;
            LocPt.Apply();
        }
    }
}
