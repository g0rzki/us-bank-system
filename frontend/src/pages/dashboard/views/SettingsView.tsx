export default function SettingsView() {
    return (
        <div className="db-view">
            <h1 className="db-view-title">Settings</h1>
            <div className="db-settings-list">
                {['Profile', 'Security', 'Notifications', 'Privacy'].map(s => (
                    <button key={s} className="db-settings-item" disabled>
                        <span>{s}</span>
                        <span className="db-settings-arrow">›</span>
                    </button>
                ))}
            </div>
        </div>
    );
}
