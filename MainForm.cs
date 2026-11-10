
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharpCompress.Archives;
using SharpCompress.Common;
using NuGet.Versioning;

namespace SPTModManager;

public sealed class MainForm : Form
{
    private const string ForgeApi = "https://sp-mod.com/api/v0/";
    private const string SptVersion = "4.0.13";
    private static readonly HttpClient Http = CreateHttp();

    private readonly TextBox gamePathBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox searchBox = new() { Width = 280 };
    private readonly Button searchButton = new() { Text = "Найти моды", AutoSize = true };
    private readonly Button installButton = new()
    {
        Text = "Установить выбранный мод",
        AutoSize = true,
        Enabled = false
    };

    private readonly DataGridView modsGrid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoGenerateColumns = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        RowHeadersVisible = false,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
    };

    private readonly TextBox logBox = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical
    };

    private readonly Label statusLabel = new()
    {
        AutoSize = true,
        Text = $"Целевая версия SPT: {SptVersion}"
    };

    private readonly List<ModItem> currentMods = new();
    private readonly DataGridView installedGrid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AutoGenerateColumns = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, RowHeadersVisible = false
    };
    private readonly DataGridView sharedBuildGrid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AutoGenerateColumns = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, RowHeadersVisible = false
    };
    private readonly TextBox buildCodeBox = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical,
        Height = 110, PlaceholderText = "Вставь сюда код сборки друга..."
    };
    private SharedBuild? importedBuild;
    private List<ModItem> importedMods = new();
    private bool busy;

    public MainForm()
    {
        Text = $"SPT Mod Manager — SPT {SptVersion}";
        Width = 1050;
        Height = 680;
        MinimumSize = new Size(850, 550);
        StartPosition = FormStartPosition.CenterScreen;

        modsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Мод",
            DataPropertyName = nameof(ModItem.Name),
            Width = 260
        });

        modsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Автор",
            DataPropertyName = nameof(ModItem.Author),
            Width = 140
        });

        modsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Последняя совместимая версия",
            DataPropertyName = nameof(ModItem.LatestCompatible),
            Width = 180
        });

        modsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Загрузки",
            DataPropertyName = nameof(ModItem.Downloads),
            Width = 100
        });

        modsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Описание",
            DataPropertyName = nameof(ModItem.Teaser),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            ColumnCount = 1,
            RowCount = 6
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var pathRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            AutoSize = true
        };

        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        pathRow.Controls.Add(
            new Label
            {
                Text = "Папка SPT:",
                AutoSize = true,
                Anchor = AnchorStyles.Left
            }, 0, 0);

        pathRow.Controls.Add(gamePathBox, 1, 0);

        var browse = new Button
        {
            Text = "Обзор...",
            AutoSize = true
        };

        browse.Click += (_, _) => BrowseGameFolder();
        pathRow.Controls.Add(browse, 2, 0);

        root.Controls.Add(pathRow, 0, 0);

        var searchRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false
        };

        searchRow.Controls.Add(new Label
        {
            Text = "Поиск:",
            AutoSize = true,
            Padding = new Padding(0, 7, 0, 0)
        });

        searchRow.Controls.Add(searchBox);
        searchRow.Controls.Add(searchButton);
        searchRow.Controls.Add(statusLabel);

        root.Controls.Add(searchRow, 0, 1);
        root.Controls.Add(modsGrid, 0, 2);
        root.Controls.Add(installButton, 0, 3);
        root.Controls.Add(logBox, 0, 4);

        root.Controls.Add(new Label
        {
            Text = "Каталог: The Forge. Список фильтруется по SPT 4.0.13; точная последняя совместимая версия проверяется перед установкой.",
            AutoSize = true,
            ForeColor = Color.DimGray
        }, 0, 5);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var catalogPage = new TabPage("Каталог модов");
        catalogPage.Controls.Add(root);
        tabs.TabPages.Add(catalogPage);

        var buildsPage = new TabPage("Установленные моды и сборки");
        buildsPage.Controls.Add(CreateBuildsPage());
        tabs.TabPages.Add(buildsPage);
        Controls.Add(tabs);

        searchButton.Click += async (_, _) => await SearchModsAsync();

        searchBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await SearchModsAsync();
            }
        };

        installButton.Click += async (_, _) => await InstallSelectedAsync();
        gamePathBox.TextChanged += (_, _) => RefreshInstalledMods();

        modsGrid.SelectionChanged += (_, _) =>
        {
            installButton.Enabled =
                !busy && modsGrid.CurrentRow?.DataBoundItem is ModItem;
        };

        Shown += async (_, _) =>
        {
            TryFindGameFolder();
            RefreshInstalledMods();
            await SearchModsAsync();
        };

        Log("Менеджер ориентирован на SPT 4.0.13. Подключаюсь к каталогу The Forge...");
    }

    private static HttpClient CreateHttp()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("SPT-Mod-Manager/2.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        return client;
    }

    private void TryFindGameFolder()
    {
        var candidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\SPT",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SPT"),
            @"C:\SPT"
        };

        var found = candidates.FirstOrDefault(IsLikelySptFolder);

        if (found != null)
            gamePathBox.Text = found;
    }

    private static bool IsLikelySptFolder(string path)
    {
        return Directory.Exists(path) &&
               (
                   File.Exists(Path.Combine(path, "SPT.Server.exe")) ||
                   File.Exists(Path.Combine(path, "EscapeFromTarkov.exe")) ||
                   Directory.Exists(Path.Combine(path, "BepInEx")) ||
                   Directory.Exists(Path.Combine(path, "SPT", "user")) ||
                   Directory.Exists(Path.Combine(path, "user"))
               );
    }

    private void BrowseGameFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Выбери корневую папку SPT 4.0.13"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
            gamePathBox.Text = dialog.SelectedPath;
    }

    private async Task SearchModsAsync()
    {
        if (busy)
            return;

        try
        {
            SetBusy(true);

            currentMods.Clear();
            modsGrid.DataSource = null;

            statusLabel.Text = "Загружаю каталог...";

            string query = searchBox.Text.Trim();

            int page = 1;
            int lastPage = 1;

            do
            {
                var url = ForgeApi +
                          "mods?filter%5Bspt_version%5D=" +
                          Uri.EscapeDataString(SptVersion) +
                          "&page=" + page +
                          "&per_page=50";

                if (!string.IsNullOrWhiteSpace(query))
                    url += "&query=" + Uri.EscapeDataString(query);

                var json = await GetJsonAsync(url);
                var data = json["data"] as JsonArray;

                if (data != null)
                {
                    foreach (var node in data)
                    {
                        if (node is not JsonObject mod)
                            continue;

                        int id = ReadInt(mod["id"]);

                        if (id <= 0)
                            continue;

                        if (currentMods.Any(m => m.Id == id))
                            continue;

                        currentMods.Add(new ModItem
                        {
                            Id = id,
                            Name = ReadString(mod["name"], "Без названия"),
                            Author = ReadString(mod["owner"]?["name"], "Неизвестен"),
                            Teaser = ReadString(mod["teaser"], ""),
                            Downloads = ReadLong(mod["downloads"]).ToString("N0"),
                            LatestCompatible = "Проверяется при установке",
                            SelectedVersion = null,
                            Guid = ReadString(mod["guid"], "")
                        });
                    }
                }

                lastPage = ReadInt(json["meta"]?["last_page"], 1);

                Log($"Загружена страница {page} из {lastPage}.");

                page++;

            } while (page <= lastPage);

            modsGrid.DataSource = currentMods;

            statusLabel.Text =
                $"SPT {SptVersion} · Модов в каталоге: {currentMods.Count}";

            Log(
                $"Каталог загружен. Модов для SPT {SptVersion}: {currentMods.Count}. " +
                "Совместимая версия уточняется при установке."
            );
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Ошибка каталога";

            Log("Ошибка загрузки каталога: " + ex.Message);

            MessageBox.Show(
                "Не удалось получить каталог The Forge:\n" + ex.Message,
                "Ошибка сети",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<ModVersion?> GetLatestCompatibleVersionAsync(int modId)
    {
        var url = ForgeApi +
                  $"mod/{modId}/versions?filter%5Bspt_version%5D=" +
                  Uri.EscapeDataString(SptVersion) +
                  "&sort=-created_at&per_page=50";

        var json = await GetJsonAsync(url);
        var data = json["data"] as JsonArray;

        if (data == null)
            return null;

        var versions = data.OfType<JsonObject>()
            .Select(v => new ModVersion
            {
                Id = ReadInt(v["id"]),
                Version = ReadString(v["version"], ""),
                Link = ReadString(v["link"], ""),
                SptConstraint = ReadString(v["spt_version_constraint"], ""),
                PublishedAt = ReadString(v["published_at"], "")
            })
            .Where(v =>
                v.Id > 0 &&
                Uri.TryCreate(v.Link, UriKind.Absolute, out _))
            .Where(v => IsConstraintCompatible(v.SptConstraint, SptVersion))
            .ToList();

        return versions
            .OrderByDescending(v => ParseVersion(v.Version))
            .FirstOrDefault();
    }

    private async Task<ModVersion?> GetExactCompatibleVersionAsync(int modId, string requestedVersion)
    {
        var url = ForgeApi +
                  $"mod/{modId}/versions?filter%5Bspt_version%5D=" +
                  Uri.EscapeDataString(SptVersion) + "&sort=-created_at&per_page=100";
        var json = await GetJsonAsync(url);
        var data = json["data"] as JsonArray;
        if (data == null) return null;

        return data.OfType<JsonObject>()
            .Select(v => new ModVersion
            {
                Id = ReadInt(v["id"]),
                Version = ReadString(v["version"], ""),
                Link = ReadString(v["link"], ""),
                SptConstraint = ReadString(v["spt_version_constraint"], ""),
                PublishedAt = ReadString(v["published_at"], "")
            })
            .FirstOrDefault(v => v.Id > 0 &&
                string.Equals(v.Version, requestedVersion, StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(v.Link, UriKind.Absolute, out _) &&
                IsConstraintCompatible(v.SptConstraint, SptVersion));
    }

    private Control CreateBuildsPage()
    {
        installedGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Мод", DataPropertyName = nameof(InstalledModRecord.Name), Width = 300 });
        installedGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Установленная версия", DataPropertyName = nameof(InstalledModRecord.Version), Width = 160 });
        installedGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Дата установки", DataPropertyName = nameof(InstalledModRecord.InstalledUtc), Width = 180, DefaultCellStyle = new DataGridViewCellStyle { Format = "g" } });

        sharedBuildGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Мод в сборке", DataPropertyName = nameof(ModItem.Name), Width = 300 });
        sharedBuildGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Версия", DataPropertyName = nameof(ModItem.RequestedVersion), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        var page = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 7 };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 30));

        page.Controls.Add(new Label { Text = "Установленные через менеджер моды", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        page.Controls.Add(installedGrid, 0, 1);
        var createRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var refreshButton = new Button { Text = "Обновить список", AutoSize = true };
        refreshButton.Click += (_, _) => RefreshInstalledMods();
        var deleteModButton = new Button { Text = "Удалить выбранный мод", AutoSize = true };
        deleteModButton.Click += (_, _) => DeleteSelectedInstalledMod();
        var createCodeButton = new Button { Text = "Создать код сборки", AutoSize = true };
        createCodeButton.Click += (_, _) => CreateBuildCode();
        var copyCodeButton = new Button { Text = "Скопировать код", AutoSize = true };
        copyCodeButton.Click += (_, _) =>
        {
            if (buildCodeBox.Text.StartsWith("SPTB1:", StringComparison.Ordinal))
            {
                Clipboard.SetText(buildCodeBox.Text);
                MessageBox.Show("Код скопирован в буфер обмена.", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else MessageBox.Show("Сначала создай или вставь код сборки.", "Нет кода", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        createRow.Controls.Add(refreshButton);
        createRow.Controls.Add(deleteModButton);
        createRow.Controls.Add(createCodeButton);
        createRow.Controls.Add(copyCodeButton);
        page.Controls.Add(createRow, 0, 2);
        page.Controls.Add(new Label { Text = "Код сборки (отправь другу или вставь полученный код):", AutoSize = true }, 0, 3);
        page.Controls.Add(buildCodeBox, 0, 4);
        var importRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var readCodeButton = new Button { Text = "Прочитать код", AutoSize = true };
        readCodeButton.Click += (_, _) => ReadBuildCode();
        var installBuildButton = new Button { Text = "Установить сборку", AutoSize = true };
        installBuildButton.Click += async (_, _) => await InstallImportedBuildAsync();
        importRow.Controls.Add(readCodeButton);
        importRow.Controls.Add(installBuildButton);
        page.Controls.Add(importRow, 0, 5);
        page.Controls.Add(sharedBuildGrid, 0, 6);
        return page;
    }

    private string GetManifestPath(string root) => Path.Combine(root, "ModManagerInstalled.json");

    private List<InstalledModRecord> LoadInstalledMods(string root)
    {
        try
        {
            string path = GetManifestPath(root);
            if (!File.Exists(path)) return new List<InstalledModRecord>();
            return JsonSerializer.Deserialize<List<InstalledModRecord>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex)
        {
            Log("Не удалось прочитать список установленных модов: " + ex.Message);
            return new List<InstalledModRecord>();
        }
    }

    private void SaveInstalledMod(string root, InstalledModRecord record)
    {
        var records = LoadInstalledMods(root);
        records.RemoveAll(x => x.Id == record.Id);
        records.Add(record);
        SaveInstalledModsManifest(root, records);
    }

    private void RefreshInstalledMods()
    {
        string root = gamePathBox.Text.Trim();
        var records = IsLikelySptFolder(root)
            ? LoadInstalledMods(root)
            : new List<InstalledModRecord>();

        if (records.Count > 0)
        {
            int before = records.Count;
            int legacyRemoved = 0;

            // Older manifest entries did not store InstalledFiles. They cannot
            // be verified against the disk, so keeping them would leave stale
            // rows forever after a manual deletion. Remove these legacy rows;
            // reinstalling the mod through this version recreates a verifiable record.
            records.RemoveAll(record =>
            {
                if (record.InstalledFiles == null || record.InstalledFiles.Count == 0)
                {
                    legacyRemoved++;
                    return true;
                }

                bool anyRecordedFileExists = record.InstalledFiles.Any(relativePath =>
                {
                    try
                    {
                        string fullPath = SafeCombine(root, relativePath);
                        return File.Exists(fullPath) || Directory.Exists(fullPath);
                    }
                    catch
                    {
                        return false;
                    }
                });

                return !anyRecordedFileExists;
            });

            int removed = before - records.Count;
            if (removed > 0)
            {
                try
                {
                    File.WriteAllText(
                        GetManifestPath(root),
                        JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
                    Log($"Обновление списка: удалено устаревших записей — {removed} (старых записей без путей файлов: {legacyRemoved}).");
                }
                catch (Exception ex)
                {
                    Log("Не удалось обновить манифест установленных модов: " + ex.Message);
                }
            }
        }

        installedGrid.DataSource = null;
        installedGrid.DataSource = records.OrderBy(x => x.Name).ToList();
    }

    private void DeleteSelectedInstalledMod()
    {
        string root = gamePathBox.Text.Trim();
        if (!IsLikelySptFolder(root))
        {
            MessageBox.Show("Сначала укажи корректную папку SPT.", "Папка SPT не найдена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (installedGrid.CurrentRow?.DataBoundItem is not InstalledModRecord selected)
        {
            MessageBox.Show("Сначала выбери мод в списке.", "Мод не выбран", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Удалить мод «{selected.Name}» {selected.Version}?\n\n" +
            "Менеджер удалит только файлы, записанные для этой установки. " +
            "Файлы, которые используются другими модами из списка, будут сохранены.\n\n" +
            "Рекомендуется закрыть SPT и сервер перед удалением.",
            "Подтверждение удаления",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        var records = LoadInstalledMods(root);
        var actualRecord = records.FirstOrDefault(x => x.Id == selected.Id);
        if (actualRecord == null)
        {
            RefreshInstalledMods();
            MessageBox.Show("Запись этого мода уже отсутствует. Список обновлён.", "Нет записи", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (actualRecord.InstalledFiles == null || actualRecord.InstalledFiles.Count == 0)
        {
            MessageBox.Show(
                "У этой записи нет списка файлов, поэтому безопасно удалить файлы мода автоматически невозможно.\n" +
                "Обнови список или удали мод вручную из папки SPT.",
                "Нет данных о файлах",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var protectedFiles = records
            .Where(x => x.Id != actualRecord.Id)
            .SelectMany(x => x.InstalledFiles ?? new List<string>())
            .Select(NormalizeRelativePathForCompare)
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int deleted = 0;
        int preserved = 0;
        var failures = new List<string>();

        foreach (string relativePath in actualRecord.InstalledFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string normalized = NormalizeRelativePathForCompare(relativePath);
                if (normalized.Length == 0)
                {
                    failures.Add(relativePath + " (некорректный путь)");
                    continue;
                }

                if (protectedFiles.Contains(normalized))
                {
                    preserved++;
                    continue;
                }

                string fullPath = SafeCombine(root, relativePath);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    deleted++;
                    RemoveEmptyParentDirectories(root, Path.GetDirectoryName(fullPath));
                }
                else if (Directory.Exists(fullPath))
                {
                    // Never recursively delete an unverified directory: it may contain user files.
                    preserved++;
                }
            }
            catch (Exception ex)
            {
                failures.Add(relativePath + " — " + ex.Message);
            }
        }

        if (failures.Count == 0)
        {
            records.RemoveAll(x => x.Id == actualRecord.Id);
            try
            {
                SaveInstalledModsManifest(root, records);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Файлы удалены, но не удалось обновить манифест: " + ex.Message,
                    "Ошибка сохранения", MessageBoxButtons.OK, MessageBoxIcon.Error);
                RefreshInstalledMods();
                return;
            }

            Log($"Удалён мод {actualRecord.Name}: файлов удалено — {deleted}, защищённых общих файлов оставлено — {preserved}.");
            RefreshInstalledMods();
            MessageBox.Show(
                $"Мод «{actualRecord.Name}» удалён из списка.\nФайлов удалено: {deleted}.\nОбщих/небезопасных путей оставлено: {preserved}.",
                "Удаление завершено", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            // Keep the manifest entry so the user can retry after closing the game or fixing permissions.
            Log($"Не удалось полностью удалить {actualRecord.Name}: ошибок — {failures.Count}.");
            MessageBox.Show(
                $"Удаление выполнено не полностью.\nУдалено файлов: {deleted}.\nОшибок: {failures.Count}.\n\n" +
                string.Join("\n", failures.Take(8)) +
                "\n\nЗакрой SPT и повтори удаление.",
                "Не всё удалось удалить", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            RefreshInstalledMods();
        }
    }

    private static string NormalizeRelativePathForCompare(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string value = path.Replace('\\', '/').Trim();
        while (value.StartsWith("./", StringComparison.Ordinal)) value = value[2..];
        if (value.StartsWith('/') || Path.IsPathRooted(value)) return "";
        if (value.Split('/').Any(part => part == ".." || part == ".")) return "";
        return value.Trim('/');
    }

    private static void RemoveEmptyParentDirectories(string root, string? directory)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? current = directory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            string fullCurrent = Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(fullCurrent, fullRoot, StringComparison.OrdinalIgnoreCase)) break;
            if (!fullCurrent.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) break;
            if (!Directory.Exists(fullCurrent) || Directory.EnumerateFileSystemEntries(fullCurrent).Any()) break;
            Directory.Delete(fullCurrent);
            current = Path.GetDirectoryName(fullCurrent);
        }
    }

    private void SaveInstalledModsManifest(string root, List<InstalledModRecord> records)
    {
        File.WriteAllText(GetManifestPath(root), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void CreateBuildCode()
    {
        string root = gamePathBox.Text.Trim();
        if (!IsLikelySptFolder(root))
        {
            MessageBox.Show("Сначала укажи папку SPT на вкладке каталога.", "Папка SPT не найдена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var records = LoadInstalledMods(root);
        if (records.Count == 0)
        {
            MessageBox.Show("В списке пока нет модов, установленных через этот менеджер. Установи моды или проверь папку SPT.", "Нет модов", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var nameForm = new Form { Text = "Новая сборка", Width = 390, Height = 145, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var nameBox = new TextBox { Left = 12, Top = 12, Width = 345, Text = "Моя сборка SPT" };
        var ok = new Button { Text = "Создать", Left = 270, Top = 48, Width = 87, DialogResult = DialogResult.OK };
        nameForm.Controls.Add(nameBox); nameForm.Controls.Add(ok); nameForm.AcceptButton = ok;
        if (nameForm.ShowDialog(this) != DialogResult.OK) return;
        var build = new SharedBuild { Name = string.IsNullOrWhiteSpace(nameBox.Text) ? "SPT сборка" : nameBox.Text.Trim(), SptVersion = SptVersion, Mods = records.Select(x => new SharedBuildMod { Id = x.Id, Name = x.Name, Version = x.Version }).ToList() };
        buildCodeBox.Text = BuildShareCodec.Encode(build);
        Log($"Создан автономный код сборки «{build.Name}», модов: {build.Mods.Count}. Никакой сервер не использовался.");
        MessageBox.Show("Код сборки создан. Скопируй его целиком и отправь другу. Для больших сборок код может быть длинным.", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ReadBuildCode()
    {
        try
        {
            importedBuild = BuildShareCodec.Decode(buildCodeBox.Text);
            if (!string.Equals(importedBuild.SptVersion, SptVersion, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Сборка рассчитана на SPT {importedBuild.SptVersion}, а менеджер настроен на {SptVersion}.");
            importedMods = importedBuild.Mods.Select(x => new ModItem { Id = x.Id, Name = x.Name, RequestedVersion = x.Version, LatestCompatible = x.Version }).ToList();
            sharedBuildGrid.DataSource = null;
            sharedBuildGrid.DataSource = importedMods;
            Log($"Прочитана сборка «{importedBuild.Name}»: {importedMods.Count} модов.");
            MessageBox.Show($"Сборка: {importedBuild.Name}\nМодов: {importedMods.Count}\nSPT: {importedBuild.SptVersion}\n\nНажми «Установить сборку», чтобы скачать их.", "Сборка прочитана", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            importedBuild = null;
            importedMods.Clear();
            sharedBuildGrid.DataSource = null;
            MessageBox.Show("Не удалось прочитать код сборки:\n" + ex.Message, "Ошибка кода", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task InstallImportedBuildAsync()
    {
        if (importedBuild == null || importedMods.Count == 0)
        {
            MessageBox.Show("Сначала вставь код и нажми «Прочитать код».", "Нет сборки", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string root = gamePathBox.Text.Trim();
        if (!IsLikelySptFolder(root))
        {
            MessageBox.Show("Сначала укажи папку SPT на вкладке каталога.", "Папка SPT не найдена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var answer = MessageBox.Show($"Установить сборку «{importedBuild.Name}»?\nМодов: {importedMods.Count}\n\nБудут установлены указанные версии и их зависимости. Существующие файлы будут резервироваться.", "Подтверждение установки сборки", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        var previousSource = modsGrid.DataSource;
        try
        {
            modsGrid.DataSource = importedMods;
            for (int i = 0; i < importedMods.Count; i++)
            {
                if (IsDisposed) break;
                modsGrid.CurrentCell = modsGrid.Rows[i].Cells[0];
                Log($"Сборка: установка {i + 1}/{importedMods.Count} — {importedMods[i].Name} {importedMods[i].RequestedVersion}");
                await InstallSelectedAsync();
            }
        }
        finally
        {
            modsGrid.DataSource = previousSource;
            modsGrid.ClearSelection();
            RefreshInstalledMods();
        }
    }

    private static bool IsConstraintCompatible(string constraint, string target)
    {
        if (string.IsNullOrWhiteSpace(constraint) ||
            !NuGetVersion.TryParse(target, out var targetVersion))
            return false;

        foreach (var alternative in constraint.Split(
                     new[] { "||" },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            bool allTermsMatch = true;

            var terms = alternative.Replace(",", " ")
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries
                );

            if (terms.Length == 0)
                continue;

            foreach (var raw in terms)
            {
                string term = raw.Trim();

                if (term == "*" ||
                    term.Equals("x", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (term.StartsWith("^") || term.StartsWith("~"))
                {
                    bool caret = term[0] == '^';

                    if (!NuGetVersion.TryParse(
                            term[1..].TrimStart('v', 'V'),
                            out var lower))
                    {
                        allTermsMatch = false;
                        break;
                    }

                    var upper = caret
                        ? (lower.Major > 0
                            ? new NuGetVersion(new Version(lower.Major + 1, 0, 0))
                            : lower.Minor > 0
                                ? new NuGetVersion(new Version(0, lower.Minor + 1, 0))
                                : new NuGetVersion(new Version(0, 0, lower.Patch + 1)))
                        : new NuGetVersion(
                            new Version(lower.Major, lower.Minor + 1, 0)
                        );

                    if (targetVersion < lower || targetVersion >= upper)
                    {
                        allTermsMatch = false;
                        break;
                    }

                    continue;
                }

                string op = "";

                foreach (var candidate in new[] { ">=", "<=", ">", "<", "=" })
                {
                    if (term.StartsWith(candidate, StringComparison.Ordinal))
                    {
                        op = candidate;
                        break;
                    }
                }

                string versionText = op.Length > 0
                    ? term[op.Length..]
                    : term;

                if (!NuGetVersion.TryParse(
                        versionText.TrimStart('v', 'V'),
                        out var bound))
                {
                    allTermsMatch = false;
                    break;
                }

                int cmp = targetVersion.CompareTo(bound);

                bool ok = op switch
                {
                    ">=" => cmp >= 0,
                    "<=" => cmp <= 0,
                    ">" => cmp > 0,
                    "<" => cmp < 0,
                    _ => cmp == 0
                };

                if (!ok)
                {
                    allTermsMatch = false;
                    break;
                }
            }

            if (allTermsMatch)
                return true;
        }

        return false;
    }

    private static NuGetVersion ParseVersion(string value)
    {
        return NuGetVersion.TryParse(value, out var version)
            ? version
            : new NuGetVersion(0, 0, 0);
    }

    private async Task InstallSelectedAsync()
    {
        if (modsGrid.CurrentRow?.DataBoundItem is not ModItem mod)
            return;

        string root = gamePathBox.Text.Trim();

        if (!IsLikelySptFolder(root))
        {
            MessageBox.Show(
                "Укажи корневую папку SPT. В ней должны находиться SPT.Server.exe, BepInEx или папка SPT\\user.",
                "Папка SPT не найдена",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );

            return;
        }

        try
        {
            SetBusy(true);

            Log($"Проверяю версии {mod.Name} для SPT {SptVersion}...");

            var compatible = string.IsNullOrWhiteSpace(mod.RequestedVersion)
                ? await GetLatestCompatibleVersionAsync(mod.Id)
                : await GetExactCompatibleVersionAsync(mod.Id, mod.RequestedVersion);

            if (compatible == null)
                throw new InvalidDataException(
                    $"The Forge не вернул подтверждённую версию {mod.Name}, совместимую с SPT {SptVersion}."
                );

            mod.SelectedVersion = compatible;
            mod.LatestCompatible = compatible.Version;

            modsGrid.Refresh();

            Log(
                $"Выбрана последняя совместимая версия: " +
                $"{mod.Name} {compatible.Version}."
            );

            Log($"Проверяю зависимости: {mod.Name} {mod.LatestCompatible}...");

            var dependencyJson = await GetJsonAsync(
                ForgeApi + "mods/dependencies?mods=" +
                Uri.EscapeDataString($"{mod.Id}:{compatible.Version}") +
                "&spt_version=" + Uri.EscapeDataString(SptVersion)
            );

            var key = $"{mod.Id}:{compatible.Version}";

            var rootDeps = dependencyJson["data"]?[key] as JsonArray;

            var packages = new Dictionary<string, DownloadPackage>(
                StringComparer.OrdinalIgnoreCase
            );

            packages[key] = new DownloadPackage(
                mod.Name,
                compatible.Link,
                compatible.Version
            );

            if (rootDeps != null)
                AddDependencyTree(rootDeps, packages);

            if (packages.Values.Any(p => string.IsNullOrWhiteSpace(p.Url)))
                throw new InvalidDataException(
                    "У одной из зависимостей отсутствует ссылка на скачивание."
                );

            int depCount = packages.Count - 1;

            if (depCount > 0)
            {
                var answer = MessageBox.Show(
                    $"Мод: {mod.Name} {mod.LatestCompatible}\n" +
                    $"Зависимостей: {depCount}\n\n" +
                    "Установить мод вместе со всеми зависимостями?",
                    "Подтверждение установки",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (answer != DialogResult.Yes)
                    return;
            }

            string backupRoot = Path.Combine(
                root,
                "ModManagerBackups",
                DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")
            );

            // Track files from the main mod AND every dependency. Otherwise uninstall
            // removes only the main archive and leaves dependency files behind.
            var installed = new List<string>();

            foreach (var package in packages.Values)
            {
                Log($"Начинаю скачивание: {package.Name} {package.Version}");

                var temp = Path.Combine(
                    Path.GetTempPath(),
                    "SPTModManager",
                    Guid.NewGuid().ToString("N") + ".archive"
                );

                Directory.CreateDirectory(Path.GetDirectoryName(temp)!);

                try
                {
                    var downloadTimer = Stopwatch.StartNew();

                    await DownloadFileAsync(
                        package.Url,
                        temp,
                        progress => Log($"{package.Name}: {progress}")
                    );

                    downloadTimer.Stop();

                    Log(
                        $"Скачивание завершено: {package.Name}. " +
                        $"Время: {downloadTimer.Elapsed.TotalSeconds:F1} сек."
                    );

                    Log($"Распаковываю {package.Name}...");

                    var extractTimer = Stopwatch.StartNew();

                    // Archive extraction can involve thousands of files. Run it on a
                    // worker thread so the WinForms UI remains responsive.
                    var files = await Task.Run(() =>
                        ExtractGameFiles(temp, root, backupRoot)
                    );

                    extractTimer.Stop();

                    installed.AddRange(files);

                    Log(
                        $"{package.Name}: установлено файлов — {files.Count}. " +
                        $"Распаковка: {extractTimer.Elapsed.TotalSeconds:F1} сек."
                    );
                }
                finally
                {
                    try
                    {
                        if (File.Exists(temp))
                            File.Delete(temp);
                    }
                    catch
                    {
                    }
                }
            }

            SaveInstalledMod(root, new InstalledModRecord
            {
                Id = mod.Id,
                Name = mod.Name,
                Version = compatible.Version,
                InstalledUtc = DateTime.UtcNow,
                InstalledFiles = installed.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            });
            RefreshInstalledMods();

            Log(
                $"Установка завершена: {mod.Name}. " +
                $"Всего файлов: {installed.Count}. " +
                $"Резервные копии: {backupRoot}"
            );

            MessageBox.Show(
                $"Установлено: {mod.Name} {mod.LatestCompatible}\n" +
                $"Зависимостей: {depCount}\n" +
                $"Файлов: {installed.Count}",
                "Готово",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
        catch (Exception ex)
        {
            Log("Ошибка установки: " + ex.Message);

            MessageBox.Show(
                "Не удалось установить мод:\n" + ex.Message,
                "Ошибка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void AddDependencyTree(
        JsonArray nodes,
        Dictionary<string, DownloadPackage> packages)
    {
        foreach (var node in nodes.OfType<JsonObject>())
        {
            if (ReadBool(node["conflict"]))
                throw new InvalidDataException(
                    "Обнаружен конфликт версий зависимостей. Установка отменена."
                );

            var latest = node["latest_compatible_version"] as JsonObject;

            if (latest == null)
                throw new InvalidDataException(
                    $"Для зависимости «{ReadString(node["name"], "неизвестно")}» " +
                    $"нет версии, совместимой с SPT {SptVersion}."
                );

            string guidOrId = ReadString(
                node["guid"],
                ReadString(node["id"], "")
            );

            string version = ReadString(latest["version"], "");
            string link = ReadString(latest["link"], "");

            if (string.IsNullOrWhiteSpace(guidOrId) ||
                string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidDataException(
                    "Forge вернул некорректную информацию о зависимости."
                );
            }

            packages[guidOrId + ":" + version] = new DownloadPackage(
                ReadString(node["name"], guidOrId),
                link,
                version
            );

            if (node["dependencies"] is JsonArray nested)
                AddDependencyTree(nested, packages);
        }
    }

    private static List<string> ExtractGameFiles(
        string archivePath,
        string gameRoot,
        string backupRoot)
    {
        // Prefer the official 7-Zip engine when it is installed. It handles ZIP/7z/RAR
        // consistently and is usually much faster on large archives than a managed
        // stream-by-stream extractor. We still copy only recognized SPT paths.
        string? sevenZip = Find7ZipExecutable();
        if (sevenZip != null)
        {
            return ExtractWith7Zip(archivePath, gameRoot, backupRoot, sevenZip);
        }

        // Fallback for users who do not have 7-Zip installed. A larger copy buffer
        // noticeably reduces overhead when archives contain many large files.
        return ExtractWithSharpCompress(archivePath, gameRoot, backupRoot);
    }

    private static string? Find7ZipExecutable()
    {
        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"),
            Path.Combine(AppContext.BaseDirectory, "7z.exe"),
            Path.Combine(AppContext.BaseDirectory, "7zz.exe")
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (string directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string executable in new[] { "7z.exe", "7zz.exe" })
                {
                    string candidate = Path.Combine(directory.Trim('"'), executable);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }

        return null;
    }

    private static List<string> ExtractWith7Zip(
        string archivePath,
        string gameRoot,
        string backupRoot,
        string sevenZip)
    {
        string stagingRoot = Path.Combine(
            Path.GetTempPath(), "SPTModManager", "extract_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = sevenZip,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("x");
            startInfo.ArgumentList.Add(archivePath);
            startInfo.ArgumentList.Add("-o" + stagingRoot);
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-bd");
            startInfo.ArgumentList.Add("-bso0");
            startInfo.ArgumentList.Add("-bsp0");

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            process.WaitForExit();

            // 7-Zip uses exit code 1 for warnings (for example, a file was skipped).
            // Treat only codes >= 2 as fatal; the actual installed file count is checked below.
            if (process.ExitCode >= 2)
            {
                throw new InvalidDataException(
                    "7-Zip не смог распаковать архив (код " + process.ExitCode + "). " +
                    "Проверь архив или установи актуальную версию 7-Zip.");
            }

            var installed = new List<string>();
            foreach (string source in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories))
            {
                string archiveRelative = Path.GetRelativePath(stagingRoot, source)
                    .Replace('\\', '/');
                string? relative = NormalizeForgePath(archiveRelative);
                if (relative == null)
                    continue;

                string destination = SafeCombine(gameRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                if (File.Exists(destination))
                {
                    string backup = SafeCombine(backupRoot, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(destination, backup, true);
                }

                CopyFileFast(source, destination);
                installed.Add(relative);
            }

            if (installed.Count == 0)
            {
                throw new InvalidDataException(
                    "7-Zip распаковал архив, но в нём не найдена стандартная структура SPT/BepInEx.");
            }

            return installed;
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingRoot))
                    Directory.Delete(stagingRoot, true);
            }
            catch
            {
                // A temporary extraction folder can be cleaned up on the next run.
            }
        }
    }

    private static List<string> ExtractWithSharpCompress(
        string archivePath,
        string gameRoot,
        string backupRoot)
    {
        using var archive = ArchiveFactory.OpenArchive(archivePath);
        var installed = new List<string>();

        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
        {
            string key = (entry.Key ?? string.Empty).Replace('\\', '/');
            while (key.StartsWith('/')) key = key[1..];

            string? relative = NormalizeForgePath(key);
            if (relative == null) continue;

            string destination = SafeCombine(gameRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (File.Exists(destination))
            {
                string backup = SafeCombine(backupRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(destination, backup, true);
            }

            using var input = entry.OpenEntryStream();
            using var output = new FileStream(
                destination, FileMode.Create, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.SequentialScan);
            input.CopyTo(output, 1024 * 1024);
            installed.Add(relative);
        }

        if (installed.Count == 0)
            throw new InvalidDataException(
                "В архиве не найдена стандартная структура SPT/BepInEx. Файлы не установлены.");

        return installed;
    }

    private static void CopyFileFast(string source, string destination)
    {
        using var input = new FileStream(
            source, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);
        using var output = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.SequentialScan);
        input.CopyTo(output, 1024 * 1024);
    }

    private static string? NormalizeForgePath(string key)
    {
        var parts = key.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length; i++)
        {
            string tail = string.Join("/", parts.Skip(i));

            if (tail.StartsWith("SPT/user/", StringComparison.OrdinalIgnoreCase) ||
                tail.Equals("SPT/user", StringComparison.OrdinalIgnoreCase))
            {
                return tail;
            }

            if (tail.StartsWith("user/", StringComparison.OrdinalIgnoreCase) ||
                tail.Equals("user", StringComparison.OrdinalIgnoreCase))
            {
                return "SPT/" + tail;
            }

            if (tail.StartsWith("SPT/BepInEx/", StringComparison.OrdinalIgnoreCase) ||
                tail.Equals("SPT/BepInEx", StringComparison.OrdinalIgnoreCase))
            {
                return tail[4..];
            }

            if (tail.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase) ||
                tail.Equals("BepInEx", StringComparison.OrdinalIgnoreCase))
            {
                return tail;
            }
        }

        return null;
    }

    private static string SafeCombine(string root, string relative)
    {
        string fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        string full = Path.GetFullPath(
            Path.Combine(
                root,
                relative.Replace('/', Path.DirectorySeparatorChar)
            )
        );

        if (!full.StartsWith(
                fullRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Архив содержит небезопасный путь: " + relative
            );
        }

        return full;
    }

    private static async Task<JsonObject> GetJsonAsync(string url)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            using var response = await Http.GetAsync(url);

            if ((int)response.StatusCode == 429)
            {
                var wait = response.Headers.RetryAfter?.Delta ??
                           TimeSpan.FromSeconds(5 * (attempt + 1));

                await Task.Delay(wait);
                continue;
            }

            response.EnsureSuccessStatusCode();

            return await ReadJsonObjectAsync(response);
        }

        throw new HttpRequestException(
            "The Forge продолжает возвращать ошибку 429. Попробуй позже."
        );
    }

    private static async Task<JsonObject> ReadJsonObjectAsync(
        HttpResponseMessage response)
    {
        return await JsonNode.ParseAsync(
                   await response.Content.ReadAsStreamAsync()
               ) as JsonObject
               ?? throw new InvalidDataException(
                   "Forge вернул некорректный JSON."
               );
    }

    // Скачивание архива с отображением прогресса и скорости.
    private static async Task DownloadFileAsync(
        string url,
        string path,
        Action<string>? reportProgress = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidDataException(
                "Недопустимая ссылка на архив."
            );
        }

        using var response = await Http.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead
        );

        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? -1;
        long downloadedBytes = 0;
        long previousBytes = 0;

        var totalTimer = Stopwatch.StartNew();
        var intervalTimer = Stopwatch.StartNew();

        await using var input = await response.Content.ReadAsStreamAsync();

        await using var output = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            131072,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );

        byte[] buffer = new byte[131072];

        while (true)
        {
            int bytesRead = await input.ReadAsync(buffer.AsMemory(0, buffer.Length));

            if (bytesRead == 0)
                break;

            await output.WriteAsync(buffer.AsMemory(0, bytesRead));

            downloadedBytes += bytesRead;

            if (intervalTimer.Elapsed.TotalMilliseconds >= 500)
            {
                double elapsed = Math.Max(intervalTimer.Elapsed.TotalSeconds, 0.001);
                double speed = (downloadedBytes - previousBytes) / elapsed;

                string progress = FormatDownloadProgress(
                    downloadedBytes,
                    totalBytes,
                    speed
                );

                reportProgress?.Invoke(progress);

                previousBytes = downloadedBytes;
                intervalTimer.Restart();
            }
        }

        await output.FlushAsync();

        totalTimer.Stop();

        double averageSpeed = totalTimer.Elapsed.TotalSeconds > 0
            ? downloadedBytes / totalTimer.Elapsed.TotalSeconds
            : 0;

        reportProgress?.Invoke(
            $"100% · {FormatBytes(downloadedBytes)} · " +
            $"средняя скорость {FormatSpeed(averageSpeed)}"
        );
    }

    private static string FormatDownloadProgress(
        long downloaded,
        long total,
        double speed)
    {
        string downloadedText = FormatBytes(downloaded);
        string speedText = FormatSpeed(speed);

        if (total > 0)
        {
            double percent = downloaded * 100.0 / total;

            return $"{percent:F1}% · {downloadedText} / {FormatBytes(total)} · {speedText}";
        }

        return $"{downloadedText} · {speedText}";
    }

    private static string FormatBytes(long bytes)
    {
        double mb = bytes / 1024.0 / 1024.0;

        if (mb >= 1024)
            return $"{mb / 1024:F2} ГБ";

        return $"{mb:F2} МБ";
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        double mbPerSecond = bytesPerSecond / 1024.0 / 1024.0;

        return $"{mbPerSecond:F2} МБ/с";
    }

    private void SetBusy(bool value)
    {
        busy = value;

        searchButton.Enabled = !value;

        installButton.Enabled =
            !value && modsGrid.CurrentRow?.DataBoundItem is ModItem;

        Cursor = value ? Cursors.WaitCursor : Cursors.Default;
    }

    private void Log(string message)
    {
        logBox.AppendText(
            $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}"
        );
    }

    private static string ReadString(JsonNode? n, string fallback)
    {
        return n?.ToString() is { Length: > 0 } s ? s : fallback;
    }

    private static int ReadInt(JsonNode? n, int fallback = 0)
    {
        return int.TryParse(n?.ToString(), out var v) ? v : fallback;
    }

    private static long ReadLong(JsonNode? n)
    {
        return long.TryParse(n?.ToString(), out var v) ? v : 0;
    }

    private static bool ReadBool(JsonNode? n)
    {
        return bool.TryParse(n?.ToString(), out var v) && v;
    }

    private sealed class ModItem
    {
        public int Id { get; init; }
        public string Guid { get; init; } = "";
        public string Name { get; init; } = "";
        public string Author { get; init; } = "";
        public string Teaser { get; init; } = "";
        public string Downloads { get; init; } = "";
        public string LatestCompatible { get; set; } = "";
        public string RequestedVersion { get; set; } = "";
        public ModVersion? SelectedVersion { get; set; }
    }

    private sealed class ModVersion
    {
        public int Id { get; init; }
        public string Version { get; init; } = "";
        public string Link { get; init; } = "";
        public string SptConstraint { get; init; } = "";
        public string PublishedAt { get; init; } = "";
    }

    private sealed record DownloadPackage(
        string Name,
        string Url,
        string Version
    );
}
