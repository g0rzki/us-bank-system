import { logout } from '../../api/auth';
import './DashboardPage.css';

export default function DashboardPage() {
    return (
        <div className="dashboard-container">
            <header className="dashboard-header">
                <span className="dashboard-logo">US Bank</span>
                <button className="dashboard-logout-btn" onClick={logout}>Log out</button>
            </header>
            <main className="dashboard-main">
                <h1>Dashboard</h1>
                <img src="https://media3.giphy.com/media/v1.Y2lkPTc5MGI3NjExc2p0eDk4NGFlNDhudGloNTJoc3pmbXZwajNuaXRwY3M4cWd5NmcydCZlcD12MV9pbnRlcm5hbF9naWZfYnlfaWQmY3Q9Zw/wr7oA0rSjnWuiLJOY5/giphy.gif" alt="" />
            </main>
        </div>
    );
}
