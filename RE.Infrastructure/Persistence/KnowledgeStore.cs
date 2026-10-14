using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using RE.Domain.Models;
using RE.Core;
using RE.Core.Abstractions;

namespace RE.Infrastructure.Persistence
{
    public class KnowledgeStore : IKnowledgeStore
    {
        private readonly string _dbPath;
        private bool _isInitialized = false;

        public KnowledgeStore()
        {
            _dbPath = ResolveDatabasePath();
            System.Diagnostics.Debug.WriteLine($"[KnowledgeStore] Connected database: {_dbPath}");
        }

        public string DatabasePath => _dbPath;

        public static string ResolveDatabasePath()
        {
            if (UserDataPaths.IsolatedRoot is { } isolated)
            {
                Directory.CreateDirectory(isolated);
                return Path.Combine(isolated, "knowledge.db");
            }
            string solutionDir = AgentConfigLoader.GetSolutionRootPath();
            return Path.Combine(solutionDir, "knowledge.db");
        }

        private async Task EnsureInitializedAsync()
        {
            if (_isInitialized) return;

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS TriageHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    FilePath TEXT NOT NULL,
                    FileName TEXT NOT NULL,
                    Md5 TEXT,
                    Sha256 TEXT,
                    FileSize INTEGER,
                    Magic TEXT,
                    Platform TEXT,
                    Architecture TEXT,
                    Packer TEXT,
                    Entropy REAL,
                    RecommendedPipeline TEXT,
                    RawJson TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS ChatSessions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SessionId TEXT UNIQUE NOT NULL,
                    Title TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS ChatHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    SessionId TEXT DEFAULT 'default'
                );

                CREATE TABLE IF NOT EXISTS Projects (
                    Path TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    LastOpenedAt TEXT NOT NULL
                );
            ";
            await cmd.ExecuteNonQueryAsync();

            // Safe column migration for existing databases
            try
            {
                using var altCmd = conn.CreateCommand();
                altCmd.CommandText = "ALTER TABLE ChatHistory ADD COLUMN SessionId TEXT DEFAULT 'default';";
                await altCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Column already exists, ignore
            }

            try
            {
                using var altCmd2 = conn.CreateCommand();
                altCmd2.CommandText = "ALTER TABLE ChatSessions ADD COLUMN ProjectName TEXT DEFAULT 'General';";
                await altCmd2.ExecuteNonQueryAsync();
            }
            catch
            {
            }

            try
            {
                using var altCmd3 = conn.CreateCommand();
                altCmd3.CommandText = "ALTER TABLE ChatSessions ADD COLUMN LastMessageSnippet TEXT DEFAULT '';";
                await altCmd3.ExecuteNonQueryAsync();
            }
            catch
            {
            }

            try
            {
                using var altCmd4 = conn.CreateCommand();
                altCmd4.CommandText = "ALTER TABLE ChatSessions ADD COLUMN IsPinned INTEGER DEFAULT 0;";
                await altCmd4.ExecuteNonQueryAsync();
            }
            catch
            {
            }

            // Ensure a default session exists if there are existing chat logs
            try
            {
                using var defCmd = conn.CreateCommand();
                defCmd.CommandText = @"
                    INSERT OR IGNORE INTO ChatSessions (SessionId, Title, CreatedAt, UpdatedAt)
                    SELECT 'default', 'New Conversation', @ts, @ts
                    WHERE EXISTS (SELECT 1 FROM ChatHistory WHERE SessionId = 'default' OR SessionId IS NULL)
                    AND NOT EXISTS (SELECT 1 FROM ChatSessions WHERE SessionId = 'default');
                ";
                defCmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("O"));
                await defCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Ignore
            }

            _isInitialized = true;
        }

        public async Task SaveTriageResultAsync(TriageResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.FilePath)) return;

            await EnsureInitializedAsync();

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            string rawJson = JsonSerializer.Serialize(result);
            string fileName = Path.GetFileName(result.FilePath);
            string md5 = result.Hashes.TryGetValue("md5", out var valMd5) ? valMd5 : "";
            string sha256 = result.Hashes.TryGetValue("sha256", out var valSha256) ? valSha256 : "";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO TriageHistory
                (Timestamp, FilePath, FileName, Md5, Sha256, FileSize, Magic, Platform, Architecture, Packer, Entropy, RecommendedPipeline, RawJson)
                VALUES
                (@ts, @path, @name, @md5, @sha256, @size, @magic, @platform, @arch, @packer, @entropy, @rec, @json);
            ";

            cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@path", result.FilePath);
            cmd.Parameters.AddWithValue("@name", fileName);
            cmd.Parameters.AddWithValue("@md5", md5);
            cmd.Parameters.AddWithValue("@sha256", sha256);
            cmd.Parameters.AddWithValue("@size", result.Size);
            cmd.Parameters.AddWithValue("@magic", result.Magic ?? "");
            cmd.Parameters.AddWithValue("@platform", result.Platform ?? "");
            cmd.Parameters.AddWithValue("@arch", result.Architecture ?? "");
            cmd.Parameters.AddWithValue("@packer", result.Packer ?? "");
            cmd.Parameters.AddWithValue("@entropy", result.Entropy);
            cmd.Parameters.AddWithValue("@rec", result.RecommendedPipeline ?? "");
            cmd.Parameters.AddWithValue("@json", rawJson);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<TriageResult>> GetRecentTriageResultsAsync(int limit = 20)
        {
            await EnsureInitializedAsync();

            var results = new List<TriageResult>();
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT RawJson FROM TriageHistory ORDER BY Id DESC LIMIT @limit";
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var rawJson = reader.GetString(0);
                try
                {
                    var item = JsonSerializer.Deserialize<TriageResult>(rawJson);
                    if (item != null)
                    {
                        results.Add(item);
                    }
                }
                catch
                {
                    // Ignore corrupted rows
                }
            }

            return results;
        }

        public async Task<List<TriageResult>> SearchTriageResultsAsync(string query, int limit = 50)
        {
            await EnsureInitializedAsync();

            var results = new List<TriageResult>();
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            if (string.IsNullOrWhiteSpace(query))
            {
                cmd.CommandText = "SELECT RawJson FROM TriageHistory ORDER BY Id DESC LIMIT @limit";
            }
            else
            {
                cmd.CommandText = @"
                    SELECT RawJson FROM TriageHistory
                    WHERE FileName LIKE @q OR Md5 LIKE @q OR Sha256 LIKE @q OR Packer LIKE @q OR Platform LIKE @q
                    ORDER BY Id DESC LIMIT @limit";
                cmd.Parameters.AddWithValue("@q", $"%{query.Trim()}%");
            }

            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var rawJson = reader.GetString(0);
                try
                {
                    var item = JsonSerializer.Deserialize<TriageResult>(rawJson);
                    if (item != null)
                    {
                        results.Add(item);
                    }
                }
                catch
                {
                    // Ignore corrupted rows
                }
            }

            return results;
        }

        public async Task DeleteTriageResultAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            await EnsureInitializedAsync();

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM TriageHistory WHERE FilePath = @path;";
            cmd.Parameters.AddWithValue("@path", filePath);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<ProjectInfo> AddProjectAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            await EnsureInitializedAsync();

            string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name)) name = path;

            var project = new ProjectInfo
            {
                Path = path,
                Name = name,
                CreatedAt = DateTime.UtcNow.ToString("O"),
                LastOpenedAt = DateTime.UtcNow.ToString("O")
            };

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Projects (Path, Name, CreatedAt, LastOpenedAt)
                VALUES (@path, @name, @cat, @lat)
                ON CONFLICT(Path) DO UPDATE SET LastOpenedAt = @lat;
            ";
            cmd.Parameters.AddWithValue("@path", project.Path);
            cmd.Parameters.AddWithValue("@name", project.Name);
            cmd.Parameters.AddWithValue("@cat", project.CreatedAt);
            cmd.Parameters.AddWithValue("@lat", project.LastOpenedAt);
            await cmd.ExecuteNonQueryAsync();

            return project;
        }

        public async Task<List<ProjectInfo>> GetProjectsAsync()
        {
            await EnsureInitializedAsync();

            var list = new List<ProjectInfo>();
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Path, Name, CreatedAt, LastOpenedAt FROM Projects ORDER BY LastOpenedAt DESC;";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ProjectInfo
                {
                    Path = reader.GetString(0),
                    Name = reader.GetString(1),
                    CreatedAt = reader.GetString(2),
                    LastOpenedAt = reader.GetString(3)
                });
            }

            return list;
        }

        public async Task RemoveProjectAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            await EnsureInitializedAsync();

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Projects WHERE Path = @path;";
            cmd.Parameters.AddWithValue("@path", path);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<ChatSessionInfo> CreateChatSessionAsync(string title, string projectName = "General")
        {
            await EnsureInitializedAsync();

            var session = new ChatSessionInfo
            {
                SessionId = Guid.NewGuid().ToString("N"),
                Title = string.IsNullOrWhiteSpace(title) ? "New Conversation" : title,
                ProjectName = string.IsNullOrWhiteSpace(projectName) ? "General" : projectName,
                LastMessageSnippet = "",
                IsPinned = false,
                CreatedAt = DateTime.UtcNow.ToString("O"),
                UpdatedAt = DateTime.UtcNow.ToString("O")
            };

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ChatSessions (SessionId, Title, ProjectName, LastMessageSnippet, IsPinned, CreatedAt, UpdatedAt)
                VALUES (@sid, @title, @proj, @snip, @ispinned, @cat, @uat);
            ";
            cmd.Parameters.AddWithValue("@sid", session.SessionId);
            cmd.Parameters.AddWithValue("@title", session.Title);
            cmd.Parameters.AddWithValue("@proj", session.ProjectName);
            cmd.Parameters.AddWithValue("@snip", session.LastMessageSnippet);
            cmd.Parameters.AddWithValue("@ispinned", session.IsPinned ? 1 : 0);
            cmd.Parameters.AddWithValue("@cat", session.CreatedAt);
            cmd.Parameters.AddWithValue("@uat", session.UpdatedAt);
            await cmd.ExecuteNonQueryAsync();

            return session;
        }

        public async Task<List<ChatSessionInfo>> GetChatSessionsAsync()
        {
            await EnsureInitializedAsync();

            var list = new List<ChatSessionInfo>();
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT SessionId, Title, ProjectName, LastMessageSnippet, CreatedAt, UpdatedAt, IsPinned FROM ChatSessions ORDER BY UpdatedAt DESC;";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ChatSessionInfo
                {
                    SessionId = reader.GetString(0),
                    Title = reader.GetString(1),
                    ProjectName = reader.IsDBNull(2) ? "General" : reader.GetString(2),
                    LastMessageSnippet = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    CreatedAt = reader.GetString(4),
                    UpdatedAt = reader.GetString(5),
                    IsPinned = !reader.IsDBNull(6) && reader.GetInt32(6) == 1
                });
            }

            // If no sessions exist in ChatSessions but messages exist, create default session
            if (list.Count == 0)
            {
                var def = await CreateChatSessionAsync("New Conversation");
                list.Add(def);
            }

            return list;
        }

        public async Task UpdateChatSessionTitleAsync(string sessionId, string newTitle)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(newTitle)) return;
            await EnsureInitializedAsync();

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE ChatSessions SET Title = @title, UpdatedAt = @uat WHERE SessionId = @sid;";
            cmd.Parameters.AddWithValue("@title", newTitle);
            cmd.Parameters.AddWithValue("@uat", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@sid", sessionId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateChatSessionProjectAsync(string sessionId, string projectName)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return;
            await EnsureInitializedAsync();

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE ChatSessions SET ProjectName = @proj, UpdatedAt = @uat WHERE SessionId = @sid;";
            cmd.Parameters.AddWithValue("@proj", string.IsNullOrWhiteSpace(projectName) ? "General" : projectName);
            cmd.Parameters.AddWithValue("@uat", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@sid", sessionId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ToggleChatSessionPinAsync(string sessionId, bool isPinned)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return;
            await EnsureInitializedAsync();

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE ChatSessions SET IsPinned = @ispinned WHERE SessionId = @sid;";
            cmd.Parameters.AddWithValue("@ispinned", isPinned ? 1 : 0);
            cmd.Parameters.AddWithValue("@sid", sessionId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteChatSessionAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return;
            await EnsureInitializedAsync();

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd1 = conn.CreateCommand();
            cmd1.CommandText = "DELETE FROM ChatHistory WHERE SessionId = @sid;";
            cmd1.Parameters.AddWithValue("@sid", sessionId);
            await cmd1.ExecuteNonQueryAsync();

            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = "DELETE FROM ChatSessions WHERE SessionId = @sid;";
            cmd2.Parameters.AddWithValue("@sid", sessionId);
            await cmd2.ExecuteNonQueryAsync();
        }

        public async Task SaveChatMessageAsync(string role, string content)
        {
            await SaveChatMessageAsync("default", role, content);
        }

        public async Task SaveChatMessageAsync(string sessionId, string role, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            if (string.IsNullOrWhiteSpace(sessionId)) sessionId = "default";

            await EnsureInitializedAsync();

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ChatHistory (Timestamp, Role, Content, SessionId)
                VALUES (@ts, @role, @content, @sid);
            ";

            cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@role", role ?? "User");
            cmd.Parameters.AddWithValue("@content", content);
            cmd.Parameters.AddWithValue("@sid", sessionId);

            await cmd.ExecuteNonQueryAsync();

            using var updCmd = conn.CreateCommand();
            updCmd.CommandText =
                "UPDATE ChatSessions SET UpdatedAt = @uat, LastMessageSnippet = @snip WHERE SessionId = @sid;";
            updCmd.Parameters.AddWithValue("@uat", DateTime.UtcNow.ToString("O"));

            string snippet = content ?? "";
            if (snippet.Length > 50) snippet = snippet.Substring(0, 47) + "...";
            updCmd.Parameters.AddWithValue("@snip", snippet);

            updCmd.Parameters.AddWithValue("@sid", sessionId);
            await updCmd.ExecuteNonQueryAsync();
        }

        public async Task<List<(string Role, string Content, string Timestamp)>> GetRecentChatMessagesAsync(
            int limit = 50)
        {
            return await GetChatMessagesBySessionAsync("default", limit);
        }

        public async Task<List<(string Role, string Content, string Timestamp)>> GetChatMessagesBySessionAsync(
            string sessionId, int limit = 100)
        {
            await EnsureInitializedAsync();
            if (string.IsNullOrWhiteSpace(sessionId)) sessionId = "default";

            var list = new List<(string Role, string Content, string Timestamp)>();
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Role, Content, Timestamp
                FROM (SELECT Id, Role, Content, Timestamp FROM ChatHistory WHERE SessionId = @sid ORDER BY Id DESC LIMIT @limit)
                ORDER BY Id ASC;
            ";
            cmd.Parameters.AddWithValue("@sid", sessionId);
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2)
                ));
            }

            return list;
        }

        public async Task ClearChatHistoryAsync()
        {
            await EnsureInitializedAsync();

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM ChatHistory; DELETE FROM ChatSessions;";
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
