import React, { useEffect, useState } from 'react';
import LoginPage from './components/LoginPage';
import ReportPage from './components/ReportPage';

interface User {
  id: string;
  username: string;
}

const App: React.FC = () => {
  const [isAuthenticated, setIsAuthenticated] = useState<boolean>(false);
  const [loading, setLoading] = useState<boolean>(true);
  const [user, setUser] = useState<User | null>(null);

  const API_URL = process.env.REACT_APP_API_URL || 'http://localhost:8000';
  const API_REPORT_URL = process.env.REACT_APP_REPORT_API_URL || 'http://localhost:8091'

  useEffect(() => {
    checkSession();
  }, []);

  const checkSession = async () => {
    try {
      const response = await fetch(`${API_URL}/api/auth/session`, {
        credentials: 'include',
        headers: {
          'Accept': 'application/json'
        }
      });

      if (response.ok) {
        const data = await response.json();
        setIsAuthenticated(true);
        setUser({
          id: data.userId,
          username: data.username
        });
      }
    } catch (error) {
      console.error('Session check failed:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleLogin = (userData: User) => {
    setIsAuthenticated(true);
    setUser(userData);
  };

  const handleLogout = async () => {
    try {
      await fetch(`${API_URL}/api/auth/logout`, {
        method: 'POST',
        credentials: 'include'
      });
    } catch (error) {
      console.error('Logout error:', error);
    } finally {
      setIsAuthenticated(false);
      setUser(null);
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-xl">Loading...</div>
      </div>
    );
  }

  return (
    <div className="App">
      {isAuthenticated ? (
        <ReportPage
          onLogout={handleLogout}
          user={user}
          apiUrl={API_REPORT_URL}
        />
      ) : (
        <LoginPage
          onLoginSuccess={handleLogin}
          apiUrl={API_URL}
        />
      )}
    </div>
  );
};

export default App;