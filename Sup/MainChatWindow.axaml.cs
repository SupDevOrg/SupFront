using Avalonia.Controls;
using Avalonia.Interactivity;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Sup.ForTokens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Sup
{
    public partial class MainChatWindow : Window
    {
        private readonly HttpClient _httpClient = HttpClientFactory.CreateAuthenticatedClient();
        private bool _isVoiceTestActive = false; // Флаг для отслеживания проверки голоса
        private WasapiCapture? _audioCapture; // Для захвата звука с микрофона
        private WasapiOut? _audioPlayback; // Для воспроизведения звука
        private int _currentPage = 0;
        private int _totalPages = 1;
        private string _currentSearchQuery = string.Empty;
        private float _smoothVolume = 0; // Сглаженное значение громкости

        public MainChatWindow(string username)
        {
            InitializeComponent();
            ResetPagination();

            UserNameLabel.Text = username;

            // События для кнопок
            SearchUsersButton.Click += OnSearchUsersClicked;
            BackToChatButton.Click += OnBackToChatClicked;
            SearchGlobalTextBox.KeyUp += async (s, e) => await OnSearchUsersAsync();
            GlobalUsersListBox.DoubleTapped += OnGlobalUserSelected;
            SettingsButton.Click += OnSettingsClicked;
            VoiceSettingsButton.Click += OnVoiceSettingsClicked;
            BackFromSettingsButton.Click += OnBackFromSettingsClicked;
            TestVoiceButton.Click += OnTestVoiceClicked;

            // Обработчики для кнопок пагинации
            PrevPageButton.Click += OnPrevPageClick;
            NextPageButton.Click += OnNextPageClick;
        }


        // Поиск ////////////////////////////////////

        private async Task PerformSearchAsync(int page = 0)
        {
            var query = SearchGlobalTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                GlobalUsersListBox.ItemsSource = new List<string>();
                ResetPagination();
                return;
            }

            try
            {
                // Сохраняем текущий запрос
                _currentSearchQuery = query;
                _currentPage = page;

                // URL с пагинацией
                var url = $"{App.ApiBaseUrl}user/{Uri.EscapeDataString(query)}?page={page}&size=8";
                var response = await _httpClient.GetAsync(url);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    TokenManager.ClearTokens();
                    GlobalUsersListBox.ItemsSource = new List<string> { "Сессия истекла. Войдите снова." };
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Ошибка поиска: {response.StatusCode}, {errorContent}");
                    GlobalUsersListBox.ItemsSource = new List<string> { $"Ошибка поиска: {response.StatusCode}" };
                    return;
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                var searchResponse = await JsonSerializer.DeserializeAsync<SearchUsersResponse>(stream);

                // Обновляем информацию о пагинации
                if (searchResponse != null)
                {
                    _totalPages = searchResponse.TotalPages;
                    UpdatePaginationInfo();

                    // Извлекаем только имена пользователей из ответа
                    var usernames = searchResponse.Users?.Select(u => u.Username).ToList() ?? new List<string>();
                    GlobalUsersListBox.ItemsSource = usernames;
                }
                else
                {
                    ResetPagination();
                    GlobalUsersListBox.ItemsSource = new List<string>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при поиске: {ex.Message}");
                GlobalUsersListBox.ItemsSource = new List<string> { "Ошибка сети" };
                ResetPagination();
            }
        }

        // Метод для обновления информации о страницах
        private void UpdatePaginationInfo()
        {
            PageInfoText.Text = $"Страница {_currentPage + 1} из {_totalPages}";
            PrevPageButton.IsEnabled = _currentPage > 0;
            NextPageButton.IsEnabled = _currentPage < _totalPages - 1;
        }

        // Сброс пагинации
        private void ResetPagination()
        {
            _currentPage = 0;
            _totalPages = 1;
            PageInfoText.Text = "Страница 1 из 1";
            PrevPageButton.IsEnabled = false;
            NextPageButton.IsEnabled = false;
        }

        private void OnSearchUsersClicked(object? sender, RoutedEventArgs e)
        {
            GlobalSearchPanel.IsVisible = true;
            ChatPanel.IsVisible = false;
        }

        // ИСПРАВЛЕННЫЙ МЕТОД - убраны параметры
        private async Task OnSearchUsersAsync()
        {
            // Добавляем небольшую задержку чтобы не спамить запросами при быстром вводе
            await Task.Delay(300);

            // Если текст изменился во время задержки, отменяем предыдущий поиск
            var currentQuery = SearchGlobalTextBox.Text ?? string.Empty;
            if (currentQuery != _currentSearchQuery)
            {
                _ = PerformSearchAsync(0);
            }
        }

        private void OnBackToChatClicked(object? sender, RoutedEventArgs e)
        {
            GlobalSearchPanel.IsVisible = false;
            ChatPanel.IsVisible = true;
        }

        private void OnGlobalUserSelected(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (GlobalUsersListBox.SelectedItem is string user)
            {
                ChatUserName.Text = user;
                GlobalSearchPanel.IsVisible = false;
                ChatPanel.IsVisible = true;
            }
        }

        // Обработчик для кнопки "Назад"
        private void OnPrevPageClick(object? sender, RoutedEventArgs e)
        {
            if (_currentPage > 0)
            {
                _ = PerformSearchAsync(_currentPage - 1);
            }
        }

        // Обработчик для кнопки "Вперед"
        private void OnNextPageClick(object? sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPages - 1)
            {
                _ = PerformSearchAsync(_currentPage + 1);
            }
        }
        // Конец поиска ////////////////////////////////////




        private void OnBackFromSettingsClicked(object? sender, RoutedEventArgs e)
        {
            // Скрываем все панели настроек и голоса, возвращаемся к чату
            SettingsPanel.IsVisible = false;
            VoicePanel.IsVisible = false;
            MainPanels.IsVisible = true;
            LeftSearchPanel.IsVisible = true;
        }

        private void OnSettingsClicked(object? sender, RoutedEventArgs e)
        {
            // Скрываем чатовые панели и левый поиск, показываем настройки
            MainPanels.IsVisible = false;
            LeftSearchPanel.IsVisible = false;
            SettingsPanel.IsVisible = true;
        }

        private void OnVoiceSettingsClicked(object? sender, RoutedEventArgs e)
        {
            // Показываем панель настроек голоса справа, левую панель настроек не скрываем
            VoicePanel.IsVisible = true;
            // Загружаем доступные аудиоустройства
            LoadAudioDevices();
        }

        private void LoadAudioDevices()
        {
            try
            {
                // Получаем список всех доступных микрофонов через MMDeviceEnumerator
                var microphones = new List<string>();
                var enumerator = new MMDeviceEnumerator();
                var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                foreach (var device in captureDevices)
                {
                    microphones.Add(device.FriendlyName);
                }
                MicrophoneComboBox.ItemsSource = microphones;

                // Получаем список всех доступных устройств воспроизведения
                var audioOutputs = new List<string>();
                var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var device in renderDevices)
                {
                    audioOutputs.Add(device.FriendlyName);
                }
                AudioOutputComboBox.ItemsSource = audioOutputs;
            }
            catch (Exception ex)
            {
                // Если не удалось загрузить устройства, выводим сообщение об ошибке
                MicrophoneComboBox.ItemsSource = new List<string> { "Ошибка загрузки микрофонов" };
                AudioOutputComboBox.ItemsSource = new List<string> { "Ошибка загрузки устройств звука" };
            }
        }

        private void OnTestVoiceClicked(object? sender, RoutedEventArgs e)
        {
            if (!_isVoiceTestActive)
            {
                // Начало проверки голоса
                StartVoiceTest();
                TestVoiceButton.Content = "Прекратить проверку";
                _isVoiceTestActive = true;
            }
            else
            {
                // Прекращение проверки голоса
                StopVoiceTest();
                TestVoiceButton.Content = "Проверить голос";
                _isVoiceTestActive = false;
            }
        }

        private void StartVoiceTest()
        {
            try
            {
                // Получаем выбранные устройства
                var selectedMicrophone = MicrophoneComboBox.SelectedIndex;
                var selectedAudioOutput = AudioOutputComboBox.SelectedIndex;

                if (selectedMicrophone < 0 || selectedAudioOutput < 0)
                {
                    return; // Ничего не выбрано
                }

                // Получаем устройства через MMDeviceEnumerator
                var enumerator = new MMDeviceEnumerator();

                // Получаем устройство захвата (микрофон)
                var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToArray();
                var captureDevice = captureDevices[selectedMicrophone];

                // Получаем устройство воспроизведения (наушники/динамики)
                var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToArray();
                var renderDevice = renderDevices[selectedAudioOutput];

                // Создаём захват с микрофона
                _audioCapture = new WasapiCapture(captureDevice);

                // Создаём провайдер для буферизации аудио с оптимальными настройками
                var waveProvider = new BufferedWaveProvider(_audioCapture.WaveFormat);
                waveProvider.BufferLength = 65536; // Увеличенный буфер для стабильности
                waveProvider.DiscardOnBufferOverflow = true;

                // Создаём воспроизведение на выбранном устройстве с минимальной задержкой
                _audioPlayback = new WasapiOut(renderDevice, AudioClientShareMode.Shared, false, 10);

                // Переменные для расчета громкости (делаем их полями класса или захватываем в замыкании)
                double currentVolume = 0;
                double peakVolume = 0;
                DateTime lastPeakTime = DateTime.Now;

                _audioCapture.DataAvailable += (s, e) =>
                {
                    // Добавляем данные в буфер провайдера
                    waveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);

                    // РАСЧЕТ ГРОМКОСТИ - ИСПРАВЛЕННАЯ ВЕРСИЯ

                    // 1. Определяем формат данных
                    int bytesPerSample = _audioCapture.WaveFormat.BitsPerSample / 8;
                    int sampleCount = e.BytesRecorded / bytesPerSample;

                    // Если нет данных, выходим
                    if (sampleCount == 0) return;

                    double sumSquares = 0;
                    double maxAmplitude = 0;

                    // 2. Обрабатываем данные в зависимости от формата
                    if (_audioCapture.WaveFormat.Encoding == WaveFormatEncoding.Pcm)
                    {
                        if (_audioCapture.WaveFormat.BitsPerSample == 16)
                        {
                            // 16-битный PCM
                            for (int i = 0; i < e.BytesRecorded; i += 2)
                            {
                                short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                                double normalizedSample = sample / 32768.0;

                                sumSquares += normalizedSample * normalizedSample;
                                maxAmplitude = Math.Max(maxAmplitude, Math.Abs(normalizedSample));
                            }
                        }
                        else if (_audioCapture.WaveFormat.BitsPerSample == 32)
                        {
                            // 32-битный PCM
                            for (int i = 0; i < e.BytesRecorded; i += 4)
                            {
                                int sample = BitConverter.ToInt32(e.Buffer, i);
                                double normalizedSample = sample / 2147483648.0;

                                sumSquares += normalizedSample * normalizedSample;
                                maxAmplitude = Math.Max(maxAmplitude, Math.Abs(normalizedSample));
                            }
                        }
                        else if (_audioCapture.WaveFormat.BitsPerSample == 8)
                        {
                            // 8-битный PCM (unsigned)
                            for (int i = 0; i < e.BytesRecorded; i++)
                            {
                                double normalizedSample = (e.Buffer[i] - 128) / 128.0;
                                sumSquares += normalizedSample * normalizedSample;
                                maxAmplitude = Math.Max(maxAmplitude, Math.Abs(normalizedSample));
                            }
                        }
                    }
                    else if (_audioCapture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                    {
                        // 32-битный float
                        for (int i = 0; i < e.BytesRecorded; i += 4)
                        {
                            float sample = BitConverter.ToSingle(e.Buffer, i);

                            sumSquares += sample * sample;
                            maxAmplitude = Math.Max(maxAmplitude, Math.Abs(sample));
                        }
                    }

                    // 3. Расчет RMS (Root Mean Square) - правильный способ
                    double rms = Math.Sqrt(sumSquares / sampleCount);

                    // 4. Преобразование в децибелы
                    double db = rms > 0 ? 20.0 * Math.Log10(rms) : -60;

                    // 5. Нормализация для отображения (0-100%)
                    // -60 dB = 0%, 0 dB = 100%
                    double normalizedVolume = Math.Max(0, Math.Min(100, (db + 60) * 100 / 60));

                    // 6. УМНОЕ СГЛАЖИВАНИЕ для VU-метра
                    double attackCoeff = 0.3;  // Быстрое нарастание (30%)
                    double releaseCoeff = 0.1; // Медленное затухание (10%)

                    if (normalizedVolume > currentVolume)
                        currentVolume = currentVolume * (1 - attackCoeff) + normalizedVolume * attackCoeff;
                    else
                        currentVolume = currentVolume * (1 - releaseCoeff) + normalizedVolume * releaseCoeff;

                    // 7. Обработка пикового значения
                    if (normalizedVolume > peakVolume)
                    {
                        peakVolume = normalizedVolume;
                        lastPeakTime = DateTime.Now;
                    }
                    else if ((DateTime.Now - lastPeakTime).TotalMilliseconds > 1500)
                    {
                        // Плавное опускание пика через 1.5 секунды
                        peakVolume *= 0.95;
                        if (peakVolume < currentVolume)
                            peakVolume = currentVolume;
                    }

                    // 8. Обновление UI
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        // Основной индикатор - сглаженное значение
                        VoiceVolumeSlider.Value = currentVolume;

                        // Дополнительно: можно обновить пиковый индикатор если он есть
                        // PeakVolumeIndicator.Value = peakVolume;

                        // Также можно обновить текстовое отображение в дБ
                        // DbTextBlock.Text = $"{db:F1} dB";
                    });
                };

                // Обработчик ошибок захвата
                _audioCapture.RecordingStopped += (s, e) =>
                {
                    if (e.Exception != null)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            // Показать ошибку пользователю
                            VoiceVolumeSlider.Value = 0;
                            // Можно показать сообщение об ошибке
                            // MessageBox.Show($"Ошибка записи: {e.Exception.Message}");
                        });
                    }
                };

                // Инициализируем и запускаем воспроизведение
                _audioPlayback.Init(waveProvider);
                _audioPlayback.Play();

                // Запускаем захват
                _audioCapture.StartRecording();
            }
            catch (Exception ex)
            {
                // Обработка ошибок
                StopVoiceTest();

                // Логирование ошибки
                Console.WriteLine($"Ошибка запуска теста голоса: {ex.Message}");

                // Показываем ошибку пользователю
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    // MessageBox.Show($"Ошибка запуска теста голоса: {ex.Message}");
                    TestVoiceButton.Content = "Проверить голос";
                    _isVoiceTestActive = false;
                });
            }
        }

        private void StopVoiceTest()
        {
            try
            {
                _audioCapture?.StopRecording();
                _audioCapture?.Dispose();
                _audioCapture = null;

                _audioPlayback?.Stop();
                _audioPlayback?.Dispose();
                _audioPlayback = null;

                _smoothVolume = 0; // Сбрасываем сглаженное значение

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    VoiceVolumeSlider.Value = 0;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при остановке теста голоса: {ex.Message}");
            }
        }

        public class SearchUsersResponse
        {
            [JsonPropertyName("users")]
            public List<UserDto> Users { get; set; } = new();

            [JsonPropertyName("currentPage")]
            public int CurrentPage { get; set; }

            [JsonPropertyName("totalItems")]
            public int TotalItems { get; set; }

            [JsonPropertyName("totalPages")]
            public int TotalPages { get; set; }
        }

        public class UserDto
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("username")]
            public string Username { get; set; } = string.Empty;
        }

    }
}


