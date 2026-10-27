using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Radar.Core.Learning;
using Radar.Infrastructure.Gemini;
using Radar.Infrastructure.Learning;
using Radar.Infrastructure.Persistence;

namespace Radar.Desktop;

/// <summary>
/// "Aprender Gentle AI": study topics, practice exams and progress. The built-in catalog is seeded on open;
/// progress and exam history are stored in the Radar database and kept across restarts.
/// </summary>
public partial class LearningWindow : Window
{
    private const int GeneratedQuestionCount = 5;

    private readonly LearningStore _store;
    private readonly Random _random = new();

    private IReadOnlyList<StudyTopic> _topics = [];
    private IReadOnlyDictionary<long, StudyStatus> _statuses = new Dictionary<long, StudyStatus>();
    private IReadOnlyList<StudyQuestion> _questions = [];

    // Exam in progress.
    private long _attemptId;
    private long? _examTopicId;
    private IReadOnlyList<StudyQuestion> _examQuestions = [];
    private int _questionIndex;
    private readonly List<(StudyQuestion Question, bool IsCorrect)> _answers = [];

    private bool _isClosed;

    // True while the topic list is rebuilt, so reselecting a topic is not taken as the user opening it.
    private bool _isRefreshingTopics;

    public LearningWindow(RadarDatabase database)
    {
        InitializeComponent();
        _store = new LearningStore(database);

        Loaded += async (_, _) => await LoadAsync();
        Closed += (_, _) => _isClosed = true;
    }

    private async Task LoadAsync()
    {
        try
        {
            await Task.Run(() => _store.SeedCatalog(GentleAiCatalog.Topics));
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            if (!_isClosed)
            {
                StatusText.Text = $"No se pudo cargar el módulo de estudio: {ex.Message}";
            }

            return;
        }

        if (_isClosed)
        {
            return;
        }

        ShowTopics(selectTopicId: null);
        ExamTopicCombo.ItemsSource = new[] { new TopicChoice(null, "Examen general (todos los temas)") }
            .Concat(_topics.Select(t => new TopicChoice(t.Id, t.Title)))
            .ToList();
        ExamTopicCombo.SelectedIndex = 0;
    }

    /// <summary>Reads topics, statuses and questions off the UI thread.</summary>
    private async Task ReloadAsync()
    {
        (_topics, _statuses, _questions) = await Task.Run(() => (_store.GetTopics(), _store.GetStatuses(), _store.GetQuestions()));
    }

    private StudyStatus StatusOf(long topicId) => _statuses.TryGetValue(topicId, out var status) ? status : StudyStatus.Pending;

    private string TopicName(long? topicId) => topicId is { } id
        ? _topics.FirstOrDefault(t => t.Id == id)?.Title ?? "Tema desconocido"
        : "Examen general";

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        // Checked fires during InitializeComponent, before the views exist.
        if (LearnView is null || ExamsView is null || ProgressView is null)
        {
            return;
        }

        LearnView.Visibility = LearnTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ExamsView.Visibility = ExamsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ProgressView.Visibility = ProgressTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = "";

        if (ProgressTab.IsChecked == true)
        {
            _ = ShowProgressAsync();
        }
    }

    // ----- Aprender -----

    private void ShowTopics(long? selectTopicId)
    {
        var rows = _topics.Select(t => new TopicRow(t, StatusOf(t.Id))).ToList();
        var view = new ListCollectionView(rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TopicRow.Category)));
        _isRefreshingTopics = true;
        try
        {
            TopicsList.ItemsSource = view;
            TopicsList.SelectedItem = rows.FirstOrDefault(r => r.Topic.Id == selectTopicId) ?? rows.FirstOrDefault();
        }
        finally
        {
            _isRefreshingTopics = false;
        }
    }

    private async void TopicsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TopicsList.SelectedItem is not TopicRow row)
        {
            TopicPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var topic = row.Topic;
        TopicPanel.Visibility = Visibility.Visible;
        TopicTitle.Text = topic.Title;
        TopicSubtitle.Text = $"{topic.Category} · {row.StatusText}";
        WhatItIsText.Text = topic.WhatItIs;
        WhatItIsForText.Text = topic.WhatItIsFor;
        HowItWorksText.Text = topic.HowItWorks;
        ExampleText.Text = topic.Example;
        DifferencesText.Text = topic.Differences;
        ConfirmedText.Text = topic.ConfirmedFacts;
        SourceUrlText.Text = topic.SourceUrl;
        SourceReferenceText.Text = topic.SourceReference;
        MarkStudiedButton.Visibility = row.Status == StudyStatus.Studied ? Visibility.Collapsed : Visibility.Visible;
        MarkPendingButton.Visibility = row.Status == StudyStatus.Pending ? Visibility.Collapsed : Visibility.Visible;

        // Opening a pending topic starts studying it; a reload (for example after "Volver a pendiente") does not.
        if (row.Status == StudyStatus.Pending && !_isRefreshingTopics)
        {
            await SetStatusAsync(topic.Id, StudyStatus.InProgress);
        }
    }

    private async void MarkStudied_Click(object sender, RoutedEventArgs e)
    {
        if (TopicsList.SelectedItem is TopicRow row)
        {
            await SetStatusAsync(row.Topic.Id, StudyStatus.Studied);
        }
    }

    private async void MarkPending_Click(object sender, RoutedEventArgs e)
    {
        if (TopicsList.SelectedItem is TopicRow row)
        {
            await SetStatusAsync(row.Topic.Id, StudyStatus.Pending);
        }
    }

    private async Task SetStatusAsync(long topicId, StudyStatus status)
    {
        try
        {
            await Task.Run(() => _store.SetStatus(topicId, status));
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            // async void callers: never let a failure close the window.
            if (!_isClosed)
            {
                StatusText.Text = $"No se pudo guardar el estado del tema: {ex.Message}";
            }

            return;
        }

        if (!_isClosed)
        {
            ShowTopics(topicId);
        }
    }

    private void SourceLink_Click(object sender, RoutedEventArgs e)
    {
        if (TopicsList.SelectedItem is not TopicRow row
            || !Uri.TryCreate(row.Topic.SourceUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"No se pudo abrir el navegador: {ex.Message}";
        }
    }

    // ----- Exámenes -----

    private void ExamTopicCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        GenerateQuestionsButton.IsEnabled = ExamTopicCombo.SelectedItem is TopicChoice { Id: not null };
        GenerateStatus.Text = GenerateQuestionsButton.IsEnabled ? "" : "Elige un tema concreto para generar preguntas.";
    }

    private async void StartExam_Click(object sender, RoutedEventArgs e)
    {
        var topicId = (ExamTopicCombo.SelectedItem as TopicChoice)?.Id;
        var count = int.Parse(CheckedTag("Count", Count5.Parent as Panel), CultureInfo.InvariantCulture);
        var difficulty = Enum.Parse<ExamDifficulty>(CheckedTag("Difficulty", DifficultyOptions));

        var questions = ExamBuilder.Build(_questions, topicId, difficulty, count, _random);
        if (questions.Count == 0)
        {
            StatusText.Text = "No hay preguntas para ese tema y dificultad. Prueba con «Variada» o genera preguntas con IA.";
            return;
        }

        await StartExamAsync(questions, topicId, difficulty);
        if (questions.Count < count)
        {
            StatusText.Text = $"Solo hay {questions.Count} preguntas disponibles para esa combinación.";
        }
    }

    private async void RepeatLastFailed_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (last, failed) = await Task.Run(() =>
            {
                var attempt = _store.GetAttempts().FirstOrDefault(a => a.AnsweredCount > 0);
                return (attempt, attempt is null ? [] : _store.GetFailedQuestions(attempt.Id));
            });

            if (last is null)
            {
                StatusText.Text = "Todavía no has respondido ningún examen.";
                return;
            }

            if (failed.Count == 0)
            {
                StatusText.Text = "Tu último examen no tiene preguntas falladas.";
                return;
            }

            await StartExamAsync(failed, last.TopicId, ExamDifficulty.Review);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"No se pudo leer el último examen: {ex.Message}";
        }
    }

    private async void RepeatFailed_Click(object sender, RoutedEventArgs e)
    {
        var failed = _answers.Where(a => !a.IsCorrect).Select(a => a.Question).ToList();
        if (failed.Count > 0)
        {
            await StartExamAsync(failed, _examTopicId, ExamDifficulty.Review);
        }
    }

    private void NewExam_Click(object sender, RoutedEventArgs e) => ShowExamPanel(ExamSetupPanel);

    private void QuitExam_Click(object sender, RoutedEventArgs e)
    {
        // The attempt stays unfinished; its answers are already stored.
        StatusText.Text = "Examen abandonado. Las respuestas dadas quedan guardadas.";
        ShowExamPanel(ExamSetupPanel);
    }

    private async Task StartExamAsync(IReadOnlyList<StudyQuestion> questions, long? topicId, ExamDifficulty difficulty)
    {
        try
        {
            _attemptId = await Task.Run(() => _store.StartAttempt(topicId, difficulty, questions.Count));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"No se pudo empezar el examen: {ex.Message}";
            return;
        }

        _examTopicId = topicId;
        _examQuestions = questions;
        _questionIndex = 0;
        _answers.Clear();
        StatusText.Text = "";
        ShowExamPanel(QuestionPanel);
        ShowQuestion();
    }

    private void ShowQuestion()
    {
        var question = _examQuestions[_questionIndex];
        var kind = question.Kind == QuestionKind.Scenario ? "Escenario práctico" : "Selección múltiple";
        var difficulty = question.Difficulty == QuestionDifficulty.Advanced ? "Avanzada" : "Básica";
        QuestionHeader.Text = $"Pregunta {_questionIndex + 1} de {_examQuestions.Count} · {kind} · {difficulty} · {TopicName(question.TopicId)}";
        QuestionText.Text = question.Prompt;

        // Options are shuffled on every showing, so the position of the right answer gives nothing away.
        var options = question.Options.Select((text, index) => new OptionRow(index, text)).ToList();
        _random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(options));
        OptionsList.ItemsSource = options;
        OptionsList.IsHitTestVisible = true;

        FeedbackPanel.Visibility = Visibility.Collapsed;
        AnswerButton.Visibility = Visibility.Visible;
        AnswerButton.IsEnabled = false;
        NextButton.Visibility = Visibility.Collapsed;
    }

    private void OptionsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        AnswerButton.IsEnabled = OptionsList.SelectedItem is OptionRow && FeedbackPanel.Visibility != Visibility.Visible;

    private async void Answer_Click(object sender, RoutedEventArgs e)
    {
        if (OptionsList.SelectedItem is not OptionRow selected)
        {
            return;
        }

        var question = _examQuestions[_questionIndex];
        var isCorrect = question.IsCorrect(selected.Index);
        _answers.Add((question, isCorrect));
        AnswerButton.IsEnabled = false;
        OptionsList.IsHitTestVisible = false;

        try
        {
            await Task.Run(() => _store.SaveAnswer(_attemptId, question.Id, selected.Index, isCorrect));
        }
        catch (Exception ex)
        {
            if (!_isClosed)
            {
                StatusText.Text = $"No se pudo guardar la respuesta: {ex.Message}";
            }
        }

        if (_isClosed)
        {
            return;
        }

        FeedbackTitle.Text = isCorrect ? "Correcto" : "Incorrecto";
        FeedbackTitle.Foreground = (System.Windows.Media.Brush)FindResource(isCorrect ? "AccentBrush" : "ErrorBrush");
        FeedbackPanel.BorderBrush = FeedbackTitle.Foreground;
        FeedbackText.Text = isCorrect
            ? question.Explanation
            : $"Respuesta correcta: {question.Options[question.CorrectOption]}\n\n{question.Explanation}";
        FeedbackOrigin.Text = $"Origen de la pregunta: {question.Origin}";
        FeedbackPanel.Visibility = Visibility.Visible;

        AnswerButton.Visibility = Visibility.Collapsed;
        NextButton.Content = _questionIndex + 1 < _examQuestions.Count ? "Siguiente" : "Ver resultado";
        NextButton.Visibility = Visibility.Visible;
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        _questionIndex++;
        if (_questionIndex < _examQuestions.Count)
        {
            ShowQuestion();
            return;
        }

        try
        {
            await Task.Run(() => _store.FinishAttempt(_attemptId));
        }
        catch (Exception ex)
        {
            if (!_isClosed)
            {
                StatusText.Text = $"No se pudo cerrar el examen: {ex.Message}";
            }
        }

        if (!_isClosed)
        {
            ShowResult();
        }
    }

    private void ShowResult()
    {
        var correct = _answers.Count(a => a.IsCorrect);
        ScoreText.Text = $"{correct} de {_answers.Count} ({Percent(correct, _answers.Count)} %)";

        var review = ExamBuilder.TopicsToReview(_answers);
        ReviewTopicsText.Text = review.Count == 0
            ? "Ninguno: respondiste todo correctamente."
            : string.Join("\n", review.Select(id =>
                $"• {TopicName(id)} ({_answers.Count(a => !a.IsCorrect && a.Question.TopicId == id)} fallos)"));
        RepeatFailedButton.IsEnabled = review.Count > 0;
        ShowExamPanel(ResultPanel);
    }

    private void ShowExamPanel(FrameworkElement panel)
    {
        ExamSetupPanel.Visibility = panel == ExamSetupPanel ? Visibility.Visible : Visibility.Collapsed;
        QuestionPanel.Visibility = panel == QuestionPanel ? Visibility.Visible : Visibility.Collapsed;
        ResultPanel.Visibility = panel == ResultPanel ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void GenerateQuestions_Click(object sender, RoutedEventArgs e)
    {
        if (ExamTopicCombo.SelectedItem is not TopicChoice { Id: { } topicId }
            || _topics.FirstOrDefault(t => t.Id == topicId) is not { } topic)
        {
            return;
        }

        using var httpClient = GeminiClient.CreateHttpClient();
        var gemini = GeminiClient.FromEnvironment(httpClient);
        if (gemini is null)
        {
            GenerateStatus.Text =
                $"Gemini no está configurado. Define la variable de entorno {GeminiClient.ApiKeyVariable} para tu usuario de Windows y reinicia Radar.";
            return;
        }

        GenerateQuestionsButton.IsEnabled = false;
        GenerateStatus.Text = "Generando preguntas…";
        try
        {
            var existing = _questions.Where(q => q.TopicId == topicId).Select(q => q.Prompt).ToList();
            var generated = await gemini.GenerateStudyQuestionsAsync(topic, GeneratedQuestionCount, existing);
            var added = await Task.Run(() => generated.Count(q => _store.TryAddQuestion(q) is not null));
            await ReloadAsync();
            if (_isClosed)
            {
                return;
            }

            GenerateStatus.Text = generated.Count == 0
                ? "Gemini no encontró información fiable suficiente en los hechos confirmados de este tema para crear preguntas nuevas."
                : $"Se añadieron {added} preguntas nuevas ({generated.Count - added} repetidas descartadas). Origen: Gemini {gemini.Model}.";
        }
        catch (Exception ex)
        {
            if (_isClosed)
            {
                return;
            }

            GenerateStatus.Text = ex is TaskCanceledException ? "Gemini no respondió a tiempo. Inténtalo de nuevo." : ex.Message;
        }
        finally
        {
            if (!_isClosed)
            {
                GenerateQuestionsButton.IsEnabled = true;
            }
        }
    }

    private static string CheckedTag(string groupName, Panel? panel) =>
        panel?.Children.OfType<RadioButton>().FirstOrDefault(r => r.GroupName == groupName && r.IsChecked == true)?.Tag as string
        ?? throw new InvalidOperationException($"No option checked in {groupName}.");

    // ----- Mi progreso -----

    private async Task ShowProgressAsync()
    {
        IReadOnlyList<ExamAttempt> attempts;
        IReadOnlyList<QuestionStats> stats;
        try
        {
            await ReloadAsync();
            (attempts, stats) = await Task.Run(() => (_store.GetAttempts(), _store.GetQuestionStats()));
        }
        catch (Exception ex)
        {
            if (!_isClosed)
            {
                StatusText.Text = $"No se pudo cargar el progreso: {ex.Message}";
            }

            return;
        }

        if (_isClosed)
        {
            return;
        }

        var studied = _topics.Count(t => StatusOf(t.Id) == StudyStatus.Studied);
        var answered = attempts.Sum(a => a.AnsweredCount);
        var correct = attempts.Sum(a => a.CorrectCount);
        ProgressSummary.Text = answered == 0
            ? $"{studied} de {_topics.Count} temas estudiados · sin respuestas todavía"
            : $"{studied} de {_topics.Count} temas estudiados · {attempts.Count} exámenes · {Percent(correct, answered)} % de aciertos";

        AttemptsList.ItemsSource = attempts.Select(a => new AttemptRow(
            a.Difficulty == ExamDifficulty.Review ? $"Repaso de falladas · {TopicName(a.TopicId)}" : TopicName(a.TopicId),
            $"{a.StartedAtUtc.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture)} · {DifficultyText(a.Difficulty)}"
                + (a.FinishedAtUtc is null ? " · sin terminar" : ""),
            $"{a.CorrectCount}/{a.QuestionCount}")).ToList();
        NoAttemptsText.Visibility = attempts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var pending = _topics.Where(t => StatusOf(t.Id) != StudyStatus.Studied).ToList();
        PendingTopicsText.Text = pending.Count == 0
            ? "Ninguno: has estudiado todos los temas."
            : string.Join("\n", pending.Select(t => $"• {t.Title} ({StatusLabel(StatusOf(t.Id))})"));

        var topicMistakes = stats
            .GroupBy(s => s.TopicId)
            .Select(g => (TopicId: g.Key, Wrong: g.Sum(s => s.Wrong), Answered: g.Sum(s => s.Answered)))
            .Where(t => t.Wrong > 0)
            .OrderByDescending(t => t.Wrong)
            .ThenBy(t => t.TopicId)
            .ToList();
        var questionsById = _questions.ToDictionary(q => q.Id);
        var worstQuestions = stats
            .Where(s => s.Wrong > 0 && questionsById.ContainsKey(s.QuestionId))
            .OrderByDescending(s => s.Wrong)
            .ThenBy(s => s.QuestionId)
            .Take(5)
            .ToList();
        MistakesText.Text = topicMistakes.Count == 0
            ? "Sin errores registrados todavía."
            : string.Join("\n", topicMistakes.Select(t => $"• {TopicName(t.TopicId)}: {t.Wrong} errores de {t.Answered} respuestas"))
              + "\n\nPreguntas más falladas:\n"
              + string.Join("\n", worstQuestions.Select(s => $"• {questionsById[s.QuestionId].Prompt} ({s.Wrong} de {s.Answered})"));
    }

    private static int Percent(int part, int total) => total == 0 ? 0 : (int)Math.Round(100.0 * part / total);

    private static string DifficultyText(ExamDifficulty difficulty) => difficulty switch
    {
        ExamDifficulty.Basic => "Básica",
        ExamDifficulty.Advanced => "Avanzada",
        ExamDifficulty.Review => "Repaso",
        _ => "Variada",
    };

    private static string StatusLabel(StudyStatus status) => status switch
    {
        StudyStatus.Studied => "Estudiado",
        StudyStatus.InProgress => "En estudio",
        _ => "Pendiente",
    };

    /// <summary>A topic in the study list.</summary>
    private sealed record TopicRow(StudyTopic Topic, StudyStatus Status)
    {
        public string Title => Topic.Title;

        public string Category => Topic.Category;

        public string StatusText => LearningWindow.StatusLabel(Status);
    }

    /// <summary>An entry of the exam topic selector. Id is null for the general exam.</summary>
    private sealed record TopicChoice(long? Id, string Title);

    /// <summary>An answer option as shown; Index is its position in the stored question.</summary>
    private sealed record OptionRow(int Index, string Text);

    private sealed record AttemptRow(string Title, string Detail, string ScoreText);
}
