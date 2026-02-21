import React, { useState, useEffect } from 'react';

// Определяем интерфейс для пропсов
interface ReportPageProps {
  onLogout: () => Promise<void> | void;
  user: { id: string; username: string } | null;
  apiUrl: string;
}

interface Report {
  id: number;
  name: string;
  date: string;
}

// Явно указываем тип пропсов
const ReportPage: React.FC<ReportPageProps> = ({ onLogout, user, apiUrl }) => {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reports, setReports] = useState<Report[]>([]);
  const [downloading, setDownloading] = useState<number | null>(null);

  useEffect(() => {
    fetchReports();
  }, []);

  const fetchReports = async () => {
    try {
      setLoading(true);
      const response = await fetch(`${apiUrl}/api/reports`, {
        credentials: 'include',
        headers: {
          'Accept': 'application/json'
        }
      });

      if (response.status === 401) {
        await onLogout();
        return;
      }

      if (!response.ok) {
        throw new Error('Failed to fetch reports');
      }

      const data = await response.json();
      setReports(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load reports');
    } finally {
      setLoading(false);
    }
  };

  const downloadReport = async (reportId: number) => {
    try {
      setDownloading(reportId);
      setError(null);

      const response = await fetch(`${apiUrl}/api/reports/${reportId}/download`, {
        credentials: 'include',
      });

      if (response.status === 401) {
        await onLogout();
        return;
      }

      if (!response.ok) {
        throw new Error('Failed to download report');
      }

      const blob = await response.blob();
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `report-${reportId}.pdf`;
      document.body.appendChild(a);
      a.click();
      window.URL.revokeObjectURL(url);
      document.body.removeChild(a);

    } catch (err) {
      setError(err instanceof Error ? err.message : 'Download failed');
    } finally {
      setDownloading(null);
    }
  };

  return (
    <div className="min-h-screen bg-gray-100">
      <nav className="bg-white shadow-sm">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex justify-between h-16">
            <div className="flex items-center">
              <h1 className="text-xl font-semibold text-gray-900">
                Usage Reports
              </h1>
            </div>
            <div className="flex items-center space-x-4">
              <span className="text-sm text-gray-600">
                Welcome, {user?.username || 'User'}
              </span>
              <button
                onClick={onLogout}
                className="inline-flex items-center px-3 py-1 border border-transparent text-sm font-medium rounded-md text-white bg-red-600 hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500"
              >
                Logout
              </button>
            </div>
          </div>
        </div>
      </nav>

      <main className="max-w-7xl mx-auto py-6 sm:px-6 lg:px-8">
        <div className="px-4 py-6 sm:px-0">
          <div className="bg-white shadow overflow-hidden sm:rounded-md">
            {loading ? (
              <div className="text-center py-12">
                <div className="text-gray-500">Loading reports...</div>
              </div>
            ) : error ? (
              <div className="text-center py-12">
                <div className="text-red-500 mb-4">{error}</div>
                <button
                  onClick={fetchReports}
                  className="inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md text-white bg-blue-600 hover:bg-blue-700"
                >
                  Try Again
                </button>
              </div>
            ) : reports.length === 0 ? (
              <div className="text-center py-12">
                <div className="text-gray-500">No reports available</div>
              </div>
            ) : (
              <ul className="divide-y divide-gray-200">
                {reports.map((report) => (
                  <li key={report.id} className="px-6 py-4">
                    <div className="flex items-center justify-between">
                      <div>
                        <p className="text-sm font-medium text-gray-900">
                          {report.name}
                        </p>
                        <p className="text-sm text-gray-500">
                          {new Date(report.date).toLocaleDateString()}
                        </p>
                      </div>
                      <button
                        onClick={() => downloadReport(report.id)}
                        disabled={downloading === report.id}
                        className={`inline-flex items-center px-3 py-1 border border-transparent text-sm font-medium rounded-md text-white ${
                          downloading === report.id
                            ? 'bg-gray-400 cursor-not-allowed'
                            : 'bg-blue-600 hover:bg-blue-700'
                        }`}
                      >
                        {downloading === report.id ? 'Downloading...' : 'Download'}
                      </button>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      </main>
    </div>
  );
};

export default ReportPage;