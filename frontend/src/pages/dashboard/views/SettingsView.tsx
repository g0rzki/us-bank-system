interface Props {
    dark: boolean;
    onToggleTheme: () => void;
}

export default function SettingsView({ dark, onToggleTheme }: Props) {
    return (
        <div className="db-view">
            <h1 className="db-view-title">Settings</h1>
            <div className="db-settings-list">
                <button className="db-settings-item" onClick={onToggleTheme}>
                    <span>Dark mode</span>
                    <span className={`db-theme-track${dark ? ' on' : ''}`}>
                        <span className="db-theme-thumb" />
                    </span>
                </button>
            </div>
        </div>
    );
}
