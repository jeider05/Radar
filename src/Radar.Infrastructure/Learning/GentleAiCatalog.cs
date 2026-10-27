using Radar.Core.Learning;

namespace Radar.Infrastructure.Learning;

/// <summary>
/// Built-in "Aprender Gentle AI" catalog (Spanish study content). ConfirmedFacts only restate the official
/// gentle-ai documentation linked in SourceUrl; the other sections are didactic explanations built on those facts.
/// Update SourceReference whenever the content is checked against a newer version of the docs.
/// </summary>
public static class GentleAiCatalog
{
    public const string Origin = "Catálogo integrado (documentación oficial de Gentle AI)";

    private const string Reference =
        "Documentación oficial de gentle-ai, rama main, consultada el 2026-10-27. El README indica la versión v4.1.1 en su instalación; CLI de referencia instalado: 4.2.0.";

    private const string Readme = "https://github.com/Gentleman-Programming/gentle-ai";
    private const string Usage = "https://github.com/Gentleman-Programming/gentle-ai/blob/main/docs/usage.md";
    private const string Components = "https://github.com/Gentleman-Programming/gentle-ai/blob/main/docs/components.md";
    private const string EngramDoc = "https://github.com/Gentleman-Programming/gentle-ai/blob/main/docs/engram.md";
    private const string IntendedUsage = "https://github.com/Gentleman-Programming/gentle-ai/blob/main/docs/intended-usage.md";

    private const string Fundamentals = "Fundamentos";
    private const string Workflows = "Flujos de trabajo";
    private const string ComponentsCategory = "Componentes";
    private const string Security = "Seguridad";

    public static IReadOnlyList<CatalogTopic> Topics { get; } =
    [
        Topic("gentle-ai", Fundamentals, "Qué es Gentle AI y cómo se relaciona con los agentes de programación", 1, Readme,
            whatItIs: "Gentle AI es un configurador de código abierto para agentes de IA de programación. No es un agente ni un modelo: es una capa que prepara el agente que ya usas (por ejemplo Claude Code u OpenCode) con memoria, un flujo de trabajo y evidencia de revisión.",
            whatItIsFor: "Sirve para que tu agente trabaje de forma consistente y verificable: recuerda decisiones entre sesiones, sigue un flujo de trabajo definido (ODD), carga skills cuando la tarea lo pide y protege archivos sensibles, sin que tengas que configurar cada pieza a mano.",
            howItWorks: "Instalas el binario gentle-ai y eliges los agentes que ya tienes. gentle-ai escribe en la configuración de cada agente las instrucciones, skills, servidores MCP (Engram y, opcionalmente, Context7), la persona y la lista de rutas denegadas, haciendo una copia de seguridad antes de cada escritura. Después trabajas en tu agente como siempre: describes el resultado que quieres y el agente aplica esa configuración.",
            example: "Usas Claude Code. Instalas gentle-ai y ejecutas gentle-ai sync. Al abrir tu proyecto pides «agrega validación al formulario de registro»: el agente consulta Engram por decisiones previas, carga la skill adecuada, decide si la tarea es pequeña o grande según ODD y verifica el resultado con pruebas.",
            differences: "Frente a un agente (Claude Code, Codex…): Gentle AI no ejecuta tareas por sí mismo, configura al agente. Frente a un modelo (Claude, Gemini…): no genera texto. Frente a un plugin aislado: coordina varios componentes (memoria, flujo, revisión, skills, permisos) de forma coherente.",
            confirmedFacts: """
                - Se presenta como «The deterministic engineering environment for the AI agent you already use».
                - Le da al agente «memory, a workflow, and evidence».
                - «never installs an AI agent for you»: configura agentes que ya tienes instalados.
                - Integra, entre otros, Claude Code, OpenCode, Codex, Cursor, VS Code Copilot, Gemini CLI, Kiro IDE, Windsurf y Pi.
                - Hace copia de la configuración antes de cada escritura («snapshotted before every write»).
                - gentle-ai doctor es un diagnóstico de solo lectura; gentle-ai sync actualiza los recursos gestionados.
                - El código es MIT; los nombres Gentle AI y Engram son marcas registradas de Alan Buscaglia.
                """,
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Qué es Gentle AI según su documentación oficial?",
                ["Un modelo de lenguaje propio", "Un configurador que prepara el agente de programación que ya usas", "Un chatbot web para programar", "Un editor de código"],
                1, "La documentación lo define como el entorno de ingeniería para el agente que ya usas: configura agentes existentes, no es un modelo ni un editor."),
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Instala Gentle AI un agente de IA por ti?",
                ["Sí, instala Claude Code automáticamente", "Sí, trae su propio agente", "No, configura agentes que ya tienes instalados", "Solo en Windows"],
                2, "El README indica que «never installs an AI agent for you»: trabaja sobre el agente que ya usas."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: quieres saber si la configuración de Gentle AI en tu máquina está sana, pero sin modificar nada. ¿Qué haces?",
                ["Ejecutar gentle-ai sync", "Ejecutar gentle-ai doctor", "Reinstalar gentle-ai", "Borrar la configuración del agente y volver a empezar"],
                1, "gentle-ai doctor es un diagnóstico de solo lectura. sync aplica cambios en los recursos gestionados, así que no cumple la condición de no modificar nada."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: te preocupa que gentle-ai sobrescriba la configuración de tu agente y pierdas cambios. ¿Qué afirmación es correcta?",
                ["No hay ninguna protección: conviene no usarlo", "Hace una copia de la configuración antes de cada escritura", "Solo funciona si la configuración está vacía", "Guarda la copia en la nube de forma obligatoria"],
                1, "El README indica que la configuración se respalda «before every write». La afirmación sobre la nube no aparece en la documentación.")),

        Topic("agente-modelo", Fundamentals, "Diferencia entre agente, modelo, chatbot y herramientas", 2, IntendedUsage,
            whatItIs: "Explicación didáctica (la documentación de Gentle AI no define estos términos). Modelo: el sistema de IA que genera texto o código a partir de una entrada (por ejemplo Claude o Gemini). Chatbot: una interfaz conversacional que responde mensajes, normalmente sin actuar sobre tu entorno. Agente: un programa que usa un modelo en bucle para decidir pasos y ejecutarlos con herramientas hasta lograr un objetivo. Herramientas: acciones concretas que el agente puede invocar, como leer un archivo, ejecutar un comando o consultar un servidor MCP.",
            whatItIsFor: "Distinguirlos te ayuda a saber qué configura Gentle AI (el agente), qué elige el agente para pensar (el modelo) y qué le permite actuar (las herramientas), y a no confundir un chat con un sistema que modifica tu repositorio.",
            howItWorks: "El agente recibe tu objetivo, consulta al modelo qué hacer, llama a una herramienta (por ejemplo, ejecutar los tests), recibe el resultado y vuelve a consultar al modelo, repitiendo hasta terminar. En Gentle AI, el agente principal trabaja en línea por defecto y solo delega en sub-agentes por una razón concreta.",
            example: "Pides «arregla el test que falla». Un chatbot te sugiere un cambio para que lo copies. Un agente lee el test, lo ejecuta, edita el código con una herramienta de edición y vuelve a ejecutar el test para comprobar que pasa.",
            differences: "Modelo ≠ agente: el mismo agente puede usar distintos modelos. Chatbot ≠ agente: el chatbot conversa, el agente actúa con herramientas. Herramienta ≠ skill: la herramienta ejecuta una acción; la skill son instrucciones sobre cómo hacer un tipo de tarea. Gentle AI no es ninguno de ellos: configura al agente.",
            confirmedFacts: """
                - La documentación oficial no define agente, modelo, chatbot ni herramienta; esta sección es didáctica.
                - Gentle AI «configures the AI agent you already use».
                - «The parent works inline by default and delegates only for a named reason»: exploración, escritores en paralelo sobre archivos disjuntos, límites de contexto o verificación independiente de cambios de alto riesgo.
                - «File count never decides delegation».
                - Configura modelos de agentes y de roles de revisión cuando el agente lo admite.
                """,
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Cuál es la diferencia principal entre un chatbot y un agente de programación?",
                ["El chatbot usa un modelo y el agente no", "El agente usa herramientas para actuar sobre tu entorno; el chatbot solo conversa", "No hay diferencia", "El agente solo funciona sin conexión"],
                1, "Explicación didáctica: el agente ejecuta acciones (leer, editar, ejecutar) en bucle; un chatbot se limita a responder mensajes."),
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Qué es una herramienta en el contexto de un agente?",
                ["El modelo de lenguaje", "Una acción concreta que el agente puede invocar, como leer un archivo o ejecutar un comando", "La persona del agente", "El historial de la conversación"],
                1, "Explicación didáctica: las herramientas son las acciones que el agente ejecuta; el modelo decide cuál usar."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: una tarea afecta a 15 archivos pero es un cambio bien entendido y de riesgo contenido. Según Gentle AI, ¿debe el agente delegar en sub-agentes?",
                ["Sí, siempre que haya más de 10 archivos", "No por el número de archivos: solo delega por una razón concreta", "Sí, un sub-agente por archivo", "Solo si el modelo es pequeño"],
                1, "La documentación dice que «File count never decides delegation»: el agente trabaja en línea y delega solo por una razón con nombre."),
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Advanced,
                "¿Cuál de estas es una razón válida de delegación según la guía de uso de Gentle AI?",
                ["Que la tarea parezca larga", "Verificación independiente de un cambio de alto riesgo", "Que el usuario pida una lista de tareas", "Que haya muchos archivos"],
                1, "intended-usage.md enumera exploración, escritores en paralelo sobre archivos disjuntos, límites de contexto y verificación independiente de cambios de alto riesgo.")),

        Topic("odd", Workflows, "ODD (Organic Driven Development)", 3, Usage,
            whatItIs: "ODD (Organic Driven Development) es el flujo de trabajo de implementación de Gentle AI en las versiones actuales. Ajusta la ceremonia al tamaño real de la tarea: el trabajo pequeño se hace directamente y el trabajo grande se documenta para poder retomarlo.",
            whatItIsFor: "Sirve para no inflar tareas sencillas con artefactos innecesarios y, a la vez, no perder el hilo en trabajos grandes que duran varias sesiones.",
            howItWorks: "En cada petición el agente: 1) comprueba si se autoriza un cambio (explicar o investigar es solo lectura); 2) explora el código; 3) resuelve dudas; 4) clasifica la tarea: es pequeña si se entiende, su riesgo está contenido y podría retomarse con la petición y el git diff; 5) si es grande, crea odd/tasks/<feature>.md antes de escribir código, con Specs, Tasks y Log, y un espejo en Engram; 6) implementa tarea por tarea con test-first cuando hay un test determinista; 7) cierra informando qué se verificó.",
            example: "«Cambia el texto del botón Guardar» es pequeño: se edita directamente, sin documento. «Agrega un módulo de exámenes con base de datos y tres pantallas» es grande: se crea odd/tasks/examenes.md con los requisitos citados literalmente antes de tocar código.",
            differences: "Frente a SDD (Spec-Driven Development): SDD fue el flujo de versiones anteriores y se retiró en v4.0.0; hoy ODD es el único flujo de implementación. Frente a RDD: ODD organiza el trabajo y RDD aporta revisión independiente; ODD nunca activa RDD. Frente a TDD: test-first es parte de ODD, pero ODD también decide tamaño, documentación y entrega.",
            confirmedFacts: """
                - SDD se retiró en v4.0.0 y ODD es «the only implementation workflow for direct and delegated work». sync elimina los recursos sdd-* publicados; --strict-tdd también se retiró en v4.0.0.
                - ODD se aplica en cada petición; explicar, investigar o proponer sigue siendo solo lectura.
                - Una tarea es pequeña «when understood, risk is contained, and the work could be resumed from the request plus git diff».
                - «Counts of files, commands, tests, fixes, or a requested todo list never decide it».
                - El trabajo grande usa odd/tasks/<feature-name>.md, creado antes de la primera escritura, con ## Specs (S#), ## Tasks y ## Log (L1 es la petición original literal), y un espejo en Engram en odd/<feature-name>/tasks.
                - Test-first (RED, GREEN, REFACTOR) se aplica cuando existe un test determinista ejecutable y un resultado esperado claro.
                - Unas 400 líneas por tarea son solo una heurística de planificación, no un límite.
                """,
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Cuál es el flujo de implementación vigente en las versiones actuales de Gentle AI?",
                ["SDD (Spec-Driven Development)", "ODD (Organic Driven Development)", "Waterfall", "Ninguno"],
                1, "Desde v4.0.0, SDD está retirado y ODD es el único flujo de implementación."),
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "En ODD, ¿qué decide si una tarea es pequeña o grande?",
                ["El número de archivos modificados", "Si se entiende, si su riesgo está contenido y si podría retomarse con la petición y el git diff", "El número de tests", "Si el usuario pide una lista de tareas"],
                1, "La documentación dice que los conteos de archivos, comandos, tests o listas de tareas nunca deciden el tamaño."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: una funcionalidad durará varias sesiones y tiene requisitos largos. ¿Qué debe existir antes de la primera escritura de código?",
                ["Nada, se empieza a programar", "El documento odd/tasks/<feature>.md con Specs, Tasks y Log, y su espejo en Engram", "Un pull request abierto", "Una revisión RDD aprobada"],
                1, "Para trabajo grande, ODD crea el documento de la funcionalidad antes de la primera escritura y lo replica en Engram para poder retomarlo."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: alguien te dice que en Gentle AI 4.x debes usar SDD con --strict-tdd. ¿Qué respondes?",
                ["Es correcto, es el flujo por defecto", "SDD y --strict-tdd se retiraron en v4.0.0; ODD aplica test-first por defecto cuando corresponde", "Solo es correcto en Windows", "SDD sustituyó a ODD en v4.0.0"],
                1, "La documentación indica que SDD y la opción --strict-tdd se retiraron en v4.0.0 y que ODD aplica test-first por defecto.")),

        Topic("rdd", Workflows, "RDD (Receipt-Driven Development)", 4, Readme,
            whatItIs: "RDD (Receipt-Driven Development) es el mecanismo de revisión de Gentle AI: registra evidencia de revisión independiente y acotada sobre un candidato de cambio congelado.",
            whatItIsFor: "Sirve para tener una mirada externa y proporcional al riesgo antes de entregar un cambio, dejando constancia de qué se revisó exactamente.",
            howItWorks: "Tras implementar, Gentle AI evalúa el riesgo del cambio. Si es pasivo, solo hace comprobaciones estructurales, sin revisor ni consentimiento. Si es medio o alto, pide tu consentimiento para ese candidato; si aceptas, congela el cambio y lo revisa (riesgo medio con una lente; riesgo alto con las cuatro lentes 4R: Risk, Resilience, Readability y Reliability), con como máximo una corrección acotada. Si declinas, se sigue la política normal del repositorio.",
            example: "Modificas una migración de la base de datos. La evaluación lo marca como riesgo alto y te ofrece revisarlo. Aceptas, se revisa con las cuatro lentes y queda aprobado. Aun así, hacer commit o push sigue siendo decisión tuya.",
            differences: "Frente a los tests: los tests comprueban comportamiento; RDD aporta una revisión independiente del cambio. Frente a ODD: ODD organiza el trabajo y RDD lo revisa; ODD nunca activa RDD. Frente a una aprobación de entrega: un resultado de RDD no autoriza commits, push, PR ni releases.",
            confirmedFacts: """
                - RDD «records bounded, independent review evidence for a frozen change candidate».
                - Está activado por defecto y se puede desactivar («on by default and opt-out»): gentle-ai review mode disable.
                - El candidato se congela antes de revisar y la profundidad depende de una evaluación de riesgo: passive, medium (una lente) o high (lentes 4R: Risk, Resilience, Readability, Reliability).
                - Riesgo pasivo o bajo: comprobaciones estructurales silenciosas, sin revisor ni consentimiento. Medio o alto: se pide consentimiento y la revisión solo se ejecuta si lo concedes.
                - Si la evaluación falla, nunca se asume riesgo bajo.
                - La evidencia de RDD no autoriza commits, push, PR ni releases; manda la política normal del repositorio.
                - Puede añadir tiempo de revisión y coste de modelo. ODD nunca activa RDD.
                """,
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Qué registra RDD?",
                ["El historial de chat", "Evidencia de revisión independiente y acotada de un candidato de cambio congelado", "Las contraseñas del proyecto", "Las métricas de uso del agente"],
                1, "La documentación define RDD como el registro de evidencia de revisión independiente sobre un candidato congelado."),
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Qué lentes usa una revisión RDD de riesgo alto?",
                ["Solo Risk", "Risk, Resilience, Readability y Reliability (4R)", "Performance y Cost", "Ninguna, es automática"],
                1, "El README indica que el riesgo alto usa las cuatro lentes 4R."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: la revisión RDD de tu cambio termina aprobada. ¿Puede el agente hacer push automáticamente por eso?",
                ["Sí, la aprobación autoriza el push", "No: la evidencia de RDD no autoriza commits, push, PR ni releases", "Sí, pero solo a main", "Solo si el riesgo era pasivo"],
                1, "La entrega sigue la política normal del repositorio y es decisión humana; RDD no la autoriza."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: la evaluación de riesgo de un cambio falla y no devuelve un nivel. ¿Cómo debe tratarse el cambio?",
                ["Como riesgo bajo, para no molestar", "Nunca como riesgo bajo: no se infiere un riesgo menor de una evaluación fallida", "Se descarta el cambio", "Se desactiva RDD"],
                1, "La documentación indica que una evaluación fallida nunca implica riesgo bajo.")),

        Topic("contexto-memoria", Fundamentals, "Contexto frente a memoria persistente", 5, EngramDoc,
            whatItIs: "Explicación didáctica. El contexto es lo que el modelo tiene delante en la conversación actual: tus mensajes, los archivos leídos y los resultados de herramientas; es limitado y se pierde o se resume al terminar la sesión. La memoria persistente es un almacén externo (en Gentle AI, Engram) que sobrevive entre sesiones y se consulta cuando hace falta.",
            whatItIsFor: "Entender la diferencia te permite saber por qué un agente «olvida» decisiones al empezar otra sesión y cómo evitarlo guardando lo importante en memoria.",
            howItWorks: "Durante una sesión, el agente razona con su contexto. Cuando toma una decisión o descubre algo útil, lo guarda en Engram (mem_save). En una sesión nueva, recupera el contexto reciente (mem_context) y busca lo que necesita (mem_search), en lugar de depender de lo que ya no está en la conversación.",
            example: "Ayer decidiste que las migraciones se aplican con PRAGMA user_version. Hoy abres una sesión nueva: sin memoria, el agente no lo sabe y podría proponer otra estrategia; con Engram, una búsqueda recupera esa decisión.",
            differences: "Contexto: temporal, limitado y de la sesión actual. Memoria persistente: duradera, buscable y entre sesiones. Archivos del repositorio (README, documento ODD): son la fuente versionada; la memoria guarda decisiones y descubrimientos y, en ODD, una copia para retomar el trabajo.",
            confirmedFacts: """
                - La documentación no compara explícitamente contexto y memoria; esa comparación es didáctica.
                - Engram ofrece «Persistent cross-session memory via MCP».
                - Los resúmenes de sesión y mem_context permiten que la siguiente sesión retome el contexto.
                - ODD replica el documento de la funcionalidad en Engram para poder retomarlo entre sesiones.
                """,
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Qué caracteriza a la memoria persistente frente al contexto de la conversación?",
                ["Desaparece al cerrar la sesión", "Sobrevive entre sesiones y se puede consultar después", "Solo guarda imágenes", "Es lo mismo que el contexto"],
                1, "Engram es memoria persistente entre sesiones; el contexto pertenece a la conversación actual."),
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Qué herramienta de Engram se usa al empezar una sesión para recuperar el historial reciente?",
                ["mem_context", "mem_delete", "git log", "mem_stats"],
                0, "La documentación de Engram describe mem_context como el historial reciente de sesiones que se consulta al inicio."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: en una sesión larga la conversación se compacta y el agente pierde detalles de una decisión de arquitectura. ¿Qué lo habría evitado?",
                ["Escribir mensajes más largos", "Guardar la decisión en Engram con mem_save cuando se tomó", "Cambiar de modelo", "Desactivar RDD"],
                1, "Guardar las decisiones en memoria persistente permite recuperarlas aunque ya no estén en el contexto."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: necesitas la fuente de verdad versionada de los requisitos de una funcionalidad grande. ¿Dónde está en ODD?",
                ["Solo en el contexto del chat", "En el archivo odd/tasks/<feature>.md del repositorio; Engram guarda su espejo", "Solo en Engram", "En el historial del navegador"],
                1, "ODD mantiene el documento en odd/tasks/ y una copia en Engram para retomarlo.")),

        Topic("engram", ComponentsCategory, "Engram", 6, EngramDoc,
            whatItIs: "Engram es el sistema de memoria persistente para agentes de IA que instala Gentle AI. El agente guarda decisiones, descubrimientos y contexto automáticamente, y los consulta en sesiones futuras.",
            whatItIsFor: "Sirve para que el agente no repita investigaciones ni contradiga decisiones ya tomadas, y para retomar trabajo después de días o tras una compactación del contexto.",
            howItWorks: "El agente se comunica con Engram mediante MCP. Guarda con mem_save, busca con mem_search, recupera el historial reciente con mem_context, obtiene un resultado completo con mem_get_observation y cierra la sesión con mem_session_summary. Las memorias se agrupan por proyecto, detectado a partir del remoto de git, y se guardan por defecto en SQLite local.",
            example: "Tras corregir un bug, el agente guarda la causa raíz con mem_save. Una semana después, al ver un error parecido, mem_search la encuentra y el agente aplica la misma solución sin volver a investigar.",
            differences: "Frente al contexto: Engram persiste entre sesiones. Frente al historial de git: git registra cambios de código; Engram registra decisiones y aprendizajes. Frente a Context7: Context7 aporta documentación externa de librerías; Engram, conocimiento de tu proyecto.",
            confirmedFacts: """
                - «Engram works automatically»: el agente guarda decisiones, descubrimientos y contexto sin intervención manual.
                - La memoria local en SQLite es la fuente predeterminada y autoritativa; Engram Cloud es opcional.
                - Herramientas principales: mem_save, mem_search, mem_context, mem_session_summary, mem_get_observation y mem_save_prompt.
                - engram sync exporta las memorias a .engram/ (se pueden versionar con git) y engram sync --import las carga en otra máquina.
                - El proyecto se detecta desde el remoto de git, en minúsculas; fuera de un repositorio, se usa el nombre de la carpeta. Si es ambiguo, pregunta.
                - capture_prompt: false se usa para artefactos automáticos.
                """,
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Dónde guarda Engram la memoria por defecto?",
                ["En la nube obligatoriamente", "En SQLite local", "En el historial del chat", "En un archivo .env"],
                1, "La documentación indica que la memoria local en SQLite es la fuente predeterminada y autoritativa; la nube es opcional."),
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Qué herramienta usa el agente para guardar una decisión en Engram?",
                ["mem_search", "mem_save", "mem_context", "mem_get_observation"],
                1, "mem_save guarda decisiones, correcciones de bugs, descubrimientos y convenciones."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: quieres compartir las memorias de un proyecto con tu equipo mediante el repositorio. ¿Qué haces?",
                ["Copiar el chat en un README", "Ejecutar engram sync para exportarlas a .engram/ y versionarlas; el resto usa engram sync --import", "Activar Context7", "Enviar la base de datos por correo"],
                1, "La documentación describe engram sync para exportar a .engram/ y --import para cargarlas en otra máquina."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: trabajas fuera de un repositorio git. ¿Cómo determina Engram el proyecto?",
                ["No funciona fuera de git", "Usa el nombre de la carpeta", "Pide una clave de API", "Usa el nombre del usuario"],
                1, "El proyecto se detecta del remoto de git y, fuera de un repositorio, del nombre de la carpeta.")),

        Topic("skills", ComponentsCategory, "Skills", 7, Components,
            whatItIs: "Una skill es un paquete de instrucciones (un archivo SKILL.md) que enseña al agente a hacer bien un tipo de tarea concreto, como escribir tests en Go o crear un pull request.",
            whatItIsFor: "Sirve para que el agente siga las convenciones correctas en cada tipo de tarea sin que tengas que repetirlas, cargando solo las instrucciones relevantes en ese momento.",
            howItWorks: "Gentle AI incluye skills organizadas por categoría, embebidas en su binario, y las inyecta en la configuración del agente. La skill-registry construye un índice con los disparadores, el alcance y la ruta exacta de cada SKILL.md. Cuando la tarea coincide con un disparador, el agente lee esa skill antes de actuar.",
            example: "Pides «prepara un PR para esta rama». El registro indica que la skill branch-pr aplica, el agente lee su SKILL.md y sigue sus pasos (comprobar el issue, la rama y el formato) antes de crear el PR.",
            differences: "Frente a MCP: una skill son instrucciones; un servidor MCP aporta herramientas ejecutables. Frente a la persona: la persona define el tono; la skill, el procedimiento. Frente a la memoria: Engram recuerda hechos de tu proyecto; la skill describe cómo hacer una tarea.",
            confirmedFacts: """
                - Los archivos de skills están «organized by category, embedded in the binary and injected into your agent's configuration».
                - Se cargan automáticamente cuando la tarea coincide.
                - skill-registry construye «an index of installed skills with triggers, scopes, and exact SKILL.md paths».
                - Los hooks de inicio refrescan el registro en clientes compatibles; manualmente: gentle-ai skill-registry refresh --force.
                - Ejemplos incluidos: go-testing, skill-creator y skill-registry. Las skills de frameworks están en el repositorio Gentleman-Programming/Gentleman-Skills.
                """,
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Qué es una skill en Gentle AI?",
                ["Un modelo especializado", "Un paquete de instrucciones (SKILL.md) para un tipo de tarea", "Un servidor MCP", "Un tema visual"],
                1, "Las skills son instrucciones; se indexan por su ruta SKILL.md y se cargan cuando la tarea coincide."),
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Qué hace la skill-registry?",
                ["Instala modelos", "Construye un índice de skills con disparadores, alcance y rutas exactas de SKILL.md", "Borra skills antiguas", "Revisa el código"],
                1, "La documentación de componentes describe skill-registry como ese índice."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: instalaste una skill nueva y el agente no la tiene en cuenta. ¿Qué comando refresca el registro manualmente?",
                ["gentle-ai doctor", "gentle-ai skill-registry refresh --force", "engram sync", "gentle-ai review mode disable"],
                1, "La guía de uso indica gentle-ai skill-registry refresh --force para refrescar el registro manualmente."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: necesitas que el agente consulte la documentación actual de una librería. ¿Es eso trabajo de una skill?",
                ["Sí, las skills descargan documentación", "No: eso lo aporta un servidor MCP como Context7; la skill describe cómo hacer una tarea", "Sí, pero solo en Go", "No lo hace nada"],
                1, "Context7 es un servidor MCP de documentación en vivo; una skill son instrucciones de procedimiento.")),

        Topic("mcp-context7", ComponentsCategory, "MCP y Context7", 8, Components,
            whatItIs: "MCP (Model Context Protocol) es un protocolo estándar para conectar un agente con servidores que le ofrecen herramientas y datos externos (definición didáctica). Context7 es un servidor MCP que aporta documentación actualizada de frameworks y librerías.",
            whatItIsFor: "MCP permite ampliar lo que el agente puede hacer sin cambiar el agente. Context7 sirve para que el agente consulte la documentación vigente de una librería en lugar de depender de lo que el modelo aprendió en su entrenamiento.",
            howItWorks: "Gentle AI registra servidores MCP en la configuración del agente. Engram funciona mediante MCP. Context7 es opcional: install y sync añaden su entrada por defecto cuando falta y respetan las entradas que ya personalizaste, por ejemplo con otra versión fijada.",
            example: "Preguntas cómo usar una API publicada el mes pasado en una librería. Sin Context7, el modelo podría inventar o usar una API antigua; con Context7, el agente consulta la documentación actual antes de escribir código.",
            differences: "MCP frente a skill: MCP aporta herramientas; la skill aporta instrucciones. Context7 frente a Engram: Context7 trae documentación externa de librerías; Engram guarda el conocimiento de tu proyecto. Ambos usan MCP.",
            confirmedFacts: """
                - Context7 es un «MCP server for live framework/library documentation» y es opcional.
                - install y sync añaden la entrada por defecto de Context7 cuando falta y conservan las entradas personalizadas, incluidas otras versiones fijadas o @latest.
                - Engram ofrece memoria persistente «via MCP».
                - El nombre completo de MCP (Model Context Protocol) y su definición general son explicación didáctica: la documentación de Gentle AI consultada no lo desarrolla.
                """,
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Qué aporta Context7?",
                ["Memoria del proyecto", "Documentación actualizada de frameworks y librerías mediante MCP", "Revisión de código", "Protección de archivos .env"],
                1, "La documentación lo describe como servidor MCP de documentación de frameworks y librerías en vivo."),
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Cuál de estos componentes de Gentle AI funciona mediante MCP?",
                ["La persona", "Engram", "El tema visual", "El documento ODD"],
                1, "Engram ofrece memoria persistente vía MCP."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: fijaste Context7 a una versión concreta en tu configuración y ejecutas gentle-ai sync. ¿Qué ocurre?",
                ["sync sustituye tu versión por la predeterminada", "sync conserva tu entrada personalizada", "sync elimina Context7", "sync falla"],
                1, "La documentación indica que las entradas personalizadas, incluidas otras versiones fijadas, se conservan."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: quieres que el agente recuerde que tu equipo usa SQLite con migraciones por user_version. ¿Qué componente es el adecuado?",
                ["Context7", "Engram", "Un tema visual", "La lista de rutas denegadas"],
                1, "Es conocimiento propio del proyecto: va en Engram. Context7 aporta documentación externa de librerías.")),

        Topic("personas", ComponentsCategory, "Personas", 9, Components,
            whatItIs: "Una persona define cómo se comunica el agente contigo: tono, estilo y enfoque al explicar. Gentle AI ofrece personas gestionadas (Gentleman o neutral) o un modo personalizado no gestionado.",
            whatItIsFor: "Sirve para adaptar la conversación a tu forma de aprender y trabajar; por ejemplo, un mentor exigente que explica el porqué o un tono profesional neutro.",
            howItWorks: "Al instalar o sincronizar eliges la persona. Con una persona gestionada, Gentle AI inyecta y mantiene sus instrucciones. Con --persona custom, dejas de usar la persona gestionada y defines la tuya.",
            example: "Con la persona Gentleman, si propones algo incorrecto, el agente te explica por qué con argumentos técnicos y te muestra la forma correcta. Con la persona neutral, recibes lo mismo con un tono profesional sin regionalismos.",
            differences: "Persona frente a skill: la persona define cómo habla el agente; la skill, cómo hace una tarea. Persona frente a tema: los temas Gentleman y Gentleman-Cute son visuales. Persona frente a permisos: la persona no amplía ni limita lo que el agente puede hacer.",
            confirmedFacts: """
                - El componente de persona ofrece «Managed Gentleman/neutral persona injection, or unmanaged custom persona mode».
                - Opciones documentadas: --persona neutral y --persona custom; el modo custom deja de usar la persona gestionada.
                - El README describe a Gentleman como «a caring but rigorous mentor».
                - Los temas Gentleman y Gentleman-Cute son un componente distinto de la persona.
                """,
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Qué controla la persona del agente?",
                ["Los permisos de archivos", "Cómo se comunica el agente contigo: tono y estilo", "El modelo que usa", "La base de datos de Engram"],
                1, "La persona define la comunicación; no cambia permisos, modelo ni memoria."),
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Cómo describe el README a la persona Gentleman?",
                ["Un asistente silencioso", "Un mentor cercano pero riguroso", "Un revisor de seguridad", "Un traductor"],
                1, "El README la describe como «a caring but rigorous mentor»."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: quieres escribir tus propias instrucciones de tono sin que Gentle AI las gestione. ¿Qué opción usas?",
                ["--persona neutral", "--persona custom", "gentle-ai doctor", "Desactivar RDD"],
                1, "--persona custom es el modo personalizado no gestionado."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: cambias de la persona Gentleman a neutral. ¿Cambia lo que el agente tiene permitido leer o editar?",
                ["Sí, la persona neutral tiene más permisos", "No: la persona define el tono, no los permisos", "Sí, la persona Gentleman bloquea .env", "Solo en OpenCode"],
                1, "Los permisos vienen de la lista de rutas denegadas y de la configuración del agente, no de la persona.")),

        Topic("permisos", Security, "Permisos y protección de archivos sensibles", 10, Components,
            whatItIs: "Gentle AI configura una lista de rutas sensibles que el agente tiene denegadas por defecto, como claves SSH, archivos .env y credenciales.",
            whatItIsFor: "Sirve para reducir el riesgo de que el agente lea o modifique secretos por accidente mientras trabaja en tu proyecto.",
            howItWorks: "Al instalar, Gentle AI escribe la lista de denegación en la configuración de cada agente compatible. En Claude Code la añade a permissions.deny solo si esa clave no existe, y conserva las listas que ya tengas. En OpenCode y Kilocode deniega esas rutas para leer y editar. Las protecciones tienen límites: un secreto leído a través de bash o grep no queda bloqueado.",
            example: "El agente intenta leer .env con su herramienta de lectura y la regla lo deniega. Pero un comando como cat .env ejecutado por la terminal podría no estar cubierto, así que la lista no sustituye a no tener secretos en el repositorio ni a revisar los comandos.",
            differences: "Frente a .gitignore: .gitignore evita subir archivos a git; la lista de denegación evita que el agente los lea o edite. Frente a un sandbox: la lista filtra rutas en herramientas concretas, no aísla el sistema. Frente a la persona: la persona no cambia permisos.",
            confirmedFacts: """
                - La lista por defecto incluye ~/.ssh/*, **/*.pem, **/*.key, **/*.p12, **/*.pfx, **/.env*, ~/.credentials/*, ~/.aws/credentials, ~/.config/gh/hosts.yml, ~/Library/Keychains/* y **/secrets/*.
                - En Claude Code se escribe en permissions.deny solo si esa clave no existe; las listas existentes se conservan.
                - En OpenCode y Kilocode esas rutas se deniegan para read y edit.
                - Leer un secreto mediante bash o la herramienta grep no está bloqueado, y las reglas con comodines no interpretan la sintaxis de la shell.
                - La configuración se respalda antes de cada escritura; commit, push, PR y merge siguen siendo decisiones separadas del usuario.
                """,
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Cuál de estas rutas está en la lista de denegación por defecto?",
                ["src/**/*.cs", "**/.env*", "README.md", "tests/**"],
                1, "**/.env* aparece en la lista de rutas sensibles por defecto."),
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "En Claude Code, ¿cuándo escribe Gentle AI la lista en permissions.deny?",
                ["Siempre, sobrescribiendo lo que haya", "Solo si la clave permissions.deny no existe", "Nunca", "Solo con RDD activado"],
                1, "La documentación indica que se escribe solo si la clave no existe y que las listas existentes se conservan."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: el agente ejecuta «cat .env» desde la terminal. Según la documentación, ¿lo bloquea la lista de denegación?",
                ["Sí, siempre", "No necesariamente: leer un secreto mediante bash no está bloqueado", "Solo en macOS", "Sí, porque la persona lo prohíbe"],
                1, "La documentación advierte que leer secretos vía bash o grep no está bloqueado y que los comodines no interpretan la shell."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: ya tenías tu propia lista permissions.deny en Claude Code antes de instalar Gentle AI. ¿Qué pasa con ella?",
                ["Se reemplaza por la lista por defecto", "Se conserva", "Se borra", "Se combina con la nube"],
                1, "La lista se escribe solo si la clave no existe; las listas existentes se conservan.")),

        Topic("verificacion", Workflows, "Verificación y pruebas del trabajo realizado", 11, Usage,
            whatItIs: "En Gentle AI, verificar significa demostrar con resultados observados que un cambio hace lo que se pidió, en lugar de fiarse de lo que el agente dice que hizo.",
            whatItIsFor: "Sirve para detectar regresiones y errores antes de entregar, y para que cada tarea marcada como hecha tenga una prueba detrás.",
            howItWorks: "Cuando existe un test determinista ejecutable y un resultado esperado claro, ODD aplica test-first: escribe un test que falla (RED), implementa hasta que pasa (GREEN) y refactoriza con los tests en verde. Si no aplica, el agente explica la excepción y hace comprobaciones proporcionales. Antes de dar por bueno un fallo reportado, lo reproduce. Para comandos que comparten código modificado, compara la salida con la versión anterior. Una tarea solo se marca como hecha con resultados observados.",
            example: "Reportas que la exportación falla. El agente primero reproduce el fallo, escribe un test que lo demuestra (RED), corrige el código (GREEN), ejecuta la suite completa y te informa de los resultados reales, incluidos los fallos que queden.",
            differences: "Verificación frente a revisión RDD: la verificación comprueba que funciona; RDD aporta una mirada independiente sobre el riesgo del cambio. Verificación frente a autoinforme: «el agente dice que funciona» no es prueba. Verificación frente a gentle-ai doctor: doctor diagnostica la instalación de Gentle AI, no tu código.",
            confirmedFacts: """
                - Test-first se aplica por defecto cuando existe un test determinista ejecutable y un resultado esperado claro: RED, GREEN y REFACTOR. Si no, el agente explica la excepción y hace comprobaciones funcionales proporcionales.
                - El runner se registra en el documento de la funcionalidad y el agente debe «never invent a runner».
                - Marcar una tarea requiere «observed outcomes and applicable proof».
                - Los fallos reportados se reproducen antes de decidir que ya funciona.
                - Los comandos existentes que comparten código modificado se comparan con la versión anterior (stdout, stderr y código de salida).
                - «the parent verifies outcomes rather than treating self-report as proof».
                - gentle-ai doctor ofrece diagnósticos de solo lectura.
                """,
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿En qué orden se aplica test-first en ODD?",
                ["GREEN, RED, REFACTOR", "RED, GREEN, REFACTOR", "REFACTOR, RED, GREEN", "Solo GREEN"],
                1, "Primero un test que falla (RED), luego la implementación que lo hace pasar (GREEN) y después la refactorización."),
            Q(QuestionKind.MultipleChoice, QuestionDifficulty.Basic,
                "¿Qué hace falta para marcar una tarea como completada en ODD?",
                ["Que el agente diga que terminó", "Resultados observados y la prueba aplicable", "Que pase un día", "Un commit"],
                1, "La documentación exige «observed outcomes and applicable proof»; el autoinforme no basta."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: un usuario reporta un fallo y el agente cree que ya está resuelto. ¿Qué debe hacer primero?",
                ["Cerrar el reporte", "Reproducir el fallo antes de decidir que ya funciona", "Pedir otra clave de API", "Hacer push"],
                1, "La guía de uso indica que los fallos reportados se reproducen antes de decidir que ya funcionan."),
            Q(QuestionKind.Scenario, QuestionDifficulty.Advanced,
                "Escenario: cambias un parser que usan otros dos comandos que no te pidieron tocar. ¿Qué comprobación indica la documentación?",
                ["Ninguna, no se pidió tocarlos", "Comparar la salida de esos comandos con la versión anterior (stdout, stderr y código de salida)", "Solo revisar el estilo del código", "Desactivar esos comandos"],
                1, "La comparación diferencial con el código anterior detecta regresiones no pedidas en código compartido.")),
    ];

    private static CatalogTopic Topic(
        string key, string category, string title, int sortOrder, string sourceUrl,
        string whatItIs, string whatItIsFor, string howItWorks, string example, string differences, string confirmedFacts,
        params StudyQuestion[] questions) =>
        new(new StudyTopic(0, key, category, title, sortOrder, whatItIs, whatItIsFor, howItWorks, example, differences,
                confirmedFacts.Trim(), sourceUrl, Reference),
            questions);

    private static StudyQuestion Q(
        QuestionKind kind, QuestionDifficulty difficulty, string prompt, string[] options, int correctOption, string explanation) =>
        new(0, 0, kind, difficulty, prompt, options, correctOption, explanation, Origin);
}
