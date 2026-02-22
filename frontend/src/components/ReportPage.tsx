import React, { useState, useEffect } from 'react';

interface ProsthesisReport {
  userId: string;
  date: string;
  crmName: string | null;
  crmAge: number | null;
  crmGender: string | null;
  prosthesisType: string;
  muscleGroup: string;
  signalsCount: number;
  signalFrequencyAvg: number;
  signalDurationAvg: number;
  signalAmplitudeAvg: number;
  signalDurationTotal: number;
}

interface DisplayReport extends ProsthesisReport {
  signalsCountAsInt: number;
  totalDurationAsInt: number;
  avgQualityScore: number;
  activityLevel: 'High' | 'Medium' | 'Low' | 'Minimal';
  dateFormatted: string;
}

const enhanceReport = (report: ProsthesisReport): DisplayReport => {
  const signalsCount = report.signalsCount;
  
  let activityLevel: 'High' | 'Medium' | 'Low' | 'Minimal' = 'Minimal';
  if (signalsCount > 1000) activityLevel = 'High';
  else if (signalsCount > 500) activityLevel = 'Medium';
  else if (signalsCount > 100) activityLevel = 'Low';
  
  const avgQualityScore = (
    signalsCount * 0.3 + 
    report.signalAmplitudeAvg * 0.3 + 
    (report.signalDurationAvg / 100) * 0.4
  );

  return {
    ...report,
    signalsCountAsInt: signalsCount,
    totalDurationAsInt: report.signalDurationTotal,
    avgQualityScore,
    activityLevel,
    dateFormatted: new Date(report.date).toLocaleDateString('ru-RU', {
      year: 'numeric',
      month: 'long',
      day: 'numeric'
    })
  };
};

const formatDuration = (seconds: number): string => {
  if (!seconds) return '0 сек';
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const secs = seconds % 60;
  
  const parts = [];
  if (hours > 0) parts.push(`${hours} ч`);
  if (minutes > 0) parts.push(`${minutes} мин`);
  if (secs > 0 || parts.length === 0) parts.push(`${secs} сек`);
  
  return parts.join(' ');
};

const getActivityLevelColor = (level: string): string => {
  switch (level) {
    case 'High': return 'text-green-600 bg-green-100';
    case 'Medium': return 'text-yellow-600 bg-yellow-100';
    case 'Low': return 'text-orange-600 bg-orange-100';
    default: return 'text-gray-600 bg-gray-100';
  }
};

interface ReportPageProps {
  onLogout: () => Promise<void> | void;
  user: { id: string; username: string } | null;
  apiUrl: string;
}

const ReportPage: React.FC<ReportPageProps> = ({ onLogout, user, apiUrl }) => {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reports, setReports] = useState<DisplayReport[]>([]);
  
  const [filters, setFilters] = useState({
    startDate: '',
    endDate: '',
    prosthesisType: '',
    muscleGroup: ''
  });
  
  const [availableProsthesisTypes, setAvailableProsthesisTypes] = useState<string[]>([]);
  const [availableMuscleGroups, setAvailableMuscleGroups] = useState<string[]>([]);

  const fetchReports = async () => {
    setLoading(true);
    setError(null);
    
    try {
      const params = new URLSearchParams();
      if (filters.startDate) params.append('startDate', filters.startDate);
      if (filters.endDate) params.append('endDate', filters.endDate);
      if (filters.prosthesisType) params.append('prosthesisType', filters.prosthesisType);
      if (filters.muscleGroup) params.append('muscleGroup', filters.muscleGroup);
      
      const url = `${apiUrl}/api/reports/user${params.toString() ? '?' + params.toString() : ''}`;
      
      const response = await fetch(url, {
        credentials: 'include',
        headers: { 'Accept': 'application/json' }
      });

      if (response.status === 401) {
        await onLogout();
        return;
      }

      if (response.status === 404) {
        setReports([]);
        return;
      }

      if (!response.ok) {
        throw new Error(`Failed to fetch reports: ${response.statusText}`);
      }

      const data: ProsthesisReport[] = await response.json();
      
      const enhanced = data.map(enhanceReport);
      setReports(enhanced);
      
      if (data.length > 0) {
        const types = [...new Set(data.map(r => r.prosthesisType))];
        const muscles = [...new Set(data.map(r => r.muscleGroup))];
        setAvailableProsthesisTypes(types);
        setAvailableMuscleGroups(muscles);
      }
      
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load reports');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    const timer = setTimeout(() => {
      fetchReports();
    }, 500);
    return () => clearTimeout(timer);
  }, [filters]);

  useEffect(() => {
    fetchReports();
  }, []);

  return (
    <div className="min-h-screen bg-gray-100">
      <nav className="bg-white shadow-sm">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex justify-between h-16">
            <div className="flex items-center">
              <h1 className="text-xl font-semibold text-gray-900">
                BionicPro Reports
              </h1>
            </div>
            <div className="flex items-center space-x-4">
              <span className="text-sm text-gray-600">
                {user?.username}
              </span>
              <button
                onClick={onLogout}
                className="px-3 py-1 text-sm text-white bg-red-600 rounded-md hover:bg-red-700"
              >
                Logout
              </button>
            </div>
          </div>
        </div>
      </nav>

      <main className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
        <div className="bg-white shadow rounded-lg p-6 mb-6">
          <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Start Date
              </label>
              <input
                type="date"
                value={filters.startDate}
                onChange={(e) => setFilters({...filters, startDate: e.target.value})}
                className="w-full border-gray-300 rounded-md shadow-sm focus:ring-blue-500 focus:border-blue-500 sm:text-sm"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                End Date
              </label>
              <input
                type="date"
                value={filters.endDate}
                onChange={(e) => setFilters({...filters, endDate: e.target.value})}
                className="w-full border-gray-300 rounded-md shadow-sm focus:ring-blue-500 focus:border-blue-500 sm:text-sm"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Prosthesis Type
              </label>
              <select
                value={filters.prosthesisType}
                onChange={(e) => setFilters({...filters, prosthesisType: e.target.value})}
                className="w-full border-gray-300 rounded-md shadow-sm focus:ring-blue-500 focus:border-blue-500 sm:text-sm"
              >
                <option value="">All</option>
                {availableProsthesisTypes.map(type => (
                  <option key={type} value={type}>{type}</option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Muscle Group
              </label>
              <select
                value={filters.muscleGroup}
                onChange={(e) => setFilters({...filters, muscleGroup: e.target.value})}
                className="w-full border-gray-300 rounded-md shadow-sm focus:ring-blue-500 focus:border-blue-500 sm:text-sm"
              >
                <option value="">All</option>
                {availableMuscleGroups.map(group => (
                  <option key={group} value={group}>{group}</option>
                ))}
              </select>
            </div>
          </div>
        </div>

        {error && (
          <div className="bg-red-50 border border-red-200 rounded-md p-4 mb-6">
            <p className="text-sm text-red-600">{error}</p>
          </div>
        )}

        <div className="bg-white shadow rounded-lg overflow-hidden">
          <div className="px-6 py-4 border-b border-gray-200">
            <h2 className="text-lg font-medium text-gray-900">
              Usage Reports ({reports.length})
            </h2>
          </div>

          {loading ? (
            <div className="text-center py-12">
              <div className="inline-block animate-spin rounded-full h-8 w-8 border-4 border-gray-300 border-t-blue-600"></div>
              <p className="mt-2 text-gray-500">Loading...</p>
            </div>
          ) : reports.length === 0 ? (
            <div className="text-center py-12">
              <p className="text-gray-500">No reports found</p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50">
                  <tr>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Date</th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Prosthesis</th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Muscle</th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Signals</th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Avg Freq</th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Avg Amp</th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Total Duration</th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Activity</th>
                  </tr>
                </thead>
                <tbody className="bg-white divide-y divide-gray-200">
                  {reports.map((report, index) => (
                    <tr key={index} className="hover:bg-gray-50">
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                        {report.dateFormatted}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                        {report.prosthesisType}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                        {report.muscleGroup}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                        {report.signalsCount.toLocaleString()}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                        {report.signalFrequencyAvg.toFixed(2)} Hz
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                        {report.signalAmplitudeAvg.toFixed(2)}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                        {formatDuration(report.signalDurationTotal)}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap">
                        <span className={`px-2 inline-flex text-xs leading-5 font-semibold rounded-full ${getActivityLevelColor(report.activityLevel)}`}>
                          {report.activityLevel}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>

        {reports.length > 0 && (
          <div className="mt-6 grid grid-cols-1 md:grid-cols-4 gap-4">
            <div className="bg-white rounded-lg shadow p-4">
              <p className="text-sm text-gray-500">Total Signals</p>
              <p className="text-2xl font-semibold">
                {reports.reduce((sum, r) => sum + r.signalsCount, 0).toLocaleString()}
              </p>
            </div>
            <div className="bg-white rounded-lg shadow p-4">
              <p className="text-sm text-gray-500">Avg Frequency</p>
              <p className="text-2xl font-semibold">
                {(reports.reduce((sum, r) => sum + r.signalFrequencyAvg, 0) / reports.length).toFixed(2)} Hz
              </p>
            </div>
            <div className="bg-white rounded-lg shadow p-4">
              <p className="text-sm text-gray-500">Total Duration</p>
              <p className="text-2xl font-semibold">
                {formatDuration(reports.reduce((sum, r) => sum + r.signalDurationTotal, 0))}
              </p>
            </div>
            <div className="bg-white rounded-lg shadow p-4">
              <p className="text-sm text-gray-500">High Activity</p>
              <p className="text-2xl font-semibold text-green-600">
                {reports.filter(r => r.activityLevel === 'High').length}
              </p>
            </div>
          </div>
        )}
      </main>
    </div>
  );
};

export default ReportPage;