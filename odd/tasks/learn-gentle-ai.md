# Learn Gentle AI module

Objective: add an "Aprender Gentle AI" study module (topics, exams, progress) to Radar Desktop, persisted in SQLite and optionally using Gemini for grounded questions.
Branch: main (user forbade commits and push: no work-unit commits). Delivery: single local change for the user to review. Test runner: `dotnet test` (xUnit, tests/Radar.Infrastructure.Tests).

## Specs

- S1 Navigation: "Agrega una entrada “Aprender Gentle AI” en la navegación principal, con tres vistas: Aprender (catálogo de temas agrupados por categoría, con su estado de estudio), Exámenes (preguntas de selección múltiple y escenarios prácticos), Mi progreso (resultados, temas pendientes y conceptos con más errores)". Keep Radar's visual design and navigation patterns.
- S2 Catalog: the 11 topics listed by the user ("Qué es Gentle AI y cómo se relaciona con los agentes de programación", "Diferencia entre agente, modelo, chatbot y herramientas", "ODD (Organic Driven Development)", "RDD (Receipt-Driven Development)", "Contexto frente a memoria persistente", "Engram", "Skills", "MCP y Context7", "Personas", "Permisos y protección de archivos sensibles", "Verificación y pruebas del trabajo realizado"). Each explains qué es, para qué sirve, cómo funciona, ejemplo práctico, diferencias con conceptos relacionados. Store source URL and version/date. "Distingue las funciones confirmadas de las explicaciones didácticas. No presentes SDD como el flujo vigente por defecto en las versiones actuales que usan ODD."
- S3 Exams: general or per-topic; 5, 10 or 20 questions; difficulty básica, variada o avanzada; no solution shown before answering; after each answer show correct/incorrect and why; final score and topics to review; repeat failed questions. Multiple choice and practical scenarios.
- S4 Persistence: topics, questions, attempts and progress in SQLite following existing patterns; progress and history survive restarts.
- S5 Gemini: reuse existing integration; ground in official docs; "No inventes características, respuestas ni referencias"; identify content origin; avoid storing duplicate questions; say so when reliable info is insufficient.
- S6 Scope: no complex doc sync, no user system; do not alter GitHub/YouTube sources or existing explanations; do not delete SQLite data; no commit/push; run `dotnet build` and `dotnet test`; report files, tables and how to test the three views.

## Tasks

- [x] T1 (S2) Research official docs — inline. Sources: gentle-ai README (v4.1.1 install line), docs/usage.md, components.md, engram.md, intended-usage.md (branch main, consulted 2026-10-27); installed CLI 4.2.0.
- [x] T2 (S2,S4) Core learning model + schema migration 4 + LearningStore + seeded catalog — inline. RED: LearningStoreTests/GentleAiCatalogTests failed to compile (no LearningStore); GREEN: suite passed.
- [x] T3 (S3) Exam builder/scoring logic with tests — inline. Note: ExamBuilder was written before its tests (RED not observed for it); tests added right after and pass.
- [x] T4 (S5) Gemini question generation grounded in topic facts, dedupe by fingerprint — inline. RED: GenerateStudyQuestionsAsync missing; GREEN: 4 new tests pass; existing explain tests unchanged and passing.
- [x] T5 (S1,S3) WPF LearningWindow with three tabs + header entry — inline. Build only; UI not exercised interactively.
- [x] T6 (S6) build + test + README note — inline. `dotnet build Radar.slnx`: 0 errors, 0 warnings. `dotnet test Radar.slnx`: 196 passed.

Route: all inline (no Writer trigger: one dependent chain of units, no parallelism reason).

## Log

- L1 (2026-10-27) User request, verbatim summary is in the session; key verbatim constraints quoted in Specs S1-S6. "No elimines datos de SQLite, no hagas commit ni push."
- L2 Findings: SDD retired in v4.0.0, ODD is the only implementation workflow (components.md, usage.md). Docs do not define agent/model/chatbot nor compare context vs memory: those topics are marked didactic.
- L3 Tables added by migration 4: StudyTopics, StudyQuestions, StudyTopicProgress, ExamAttempts, ExamAnswers. No existing table changed.
- L4 RDD assess: high (process_boundary: Process.Start for the https source link in LearningWindow). Review lenses: risk, reliability.
- L5 RDD review (risk, reliability) found R3-pending-reverts ("Volver a pendiente" re-marked the topic as InProgress on list reload): fixed with a refresh guard in LearningWindow; the validator confirmed it. R3-display-name-regression was kept on purpose: the user asked for the DisplayName change earlier in this session. The review ended escalated (informational).
- L6 (2026-10-27) User: "has commti de todo eso porfavor". Committed on branch feat/learn-gentle-ai.
