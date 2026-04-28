import { useNavigate } from 'react-router-dom';
import { logout } from '../api/auth';

export default function DashboardPage() {
    const navigate = useNavigate();

    return (
        <div>
            <h1>Dashboard</h1>
            <button onClick={logout}>Logout</button>
        </div>
    );
}
