# Replay-Slider und App-Audio – vorbereitetes Update

Dieses Update ist im Quellcode umgesetzt. Auf Wunsch werden die distributierbare Spezi.exe und der Installer erst nach einem ausdrücklichen Go neu erstellt. Die vorhandenen Dateien in `dist` und `installer` bleiben bis dahin unverändert.

## Replay-Dauer

- 15 Sekunden bis 5 Minuten in 15-Sekunden-Schritten.
- Der Slider liegt ausschließlich unter Settings. Recording enthält die Replay-Steuerung, keine Einstellung der Clipdauer.
- 20 Punktmarkierungen; die fünf vollen Minuten haben größere Punkte.
- Der Auswahlpunkt und die genaue Zeitangabe übernehmen die aktuelle Akzentfarbe sofort.
- Vorhandene Einstellungen in Minuten werden automatisch und verlustfrei in Sekunden übernommen.
- Die Dauer lässt sich während einer Replay-Aufnahme ändern. Verkleinern trimmt den vorhandenen komprimierten Puffer sofort. Beim Vergrößern füllt sich das längere Zeitfenster mit neuen Aufnahmedaten.
- Ein gespeicherter Clip beginnt an einem Videokeyframe und kann deshalb geringfügig kürzer sein als die ausgewählte Dauer.

## App-Audio

In Einstellungen → Ton & Mikrofon lässt sich zwischen „Gesamter Systemton“ und „Nur ausgewählte Apps“ wechseln. Zur Auswahl stehen das aktuelle Spiel, Discord, TeamSpeak und weitere laufende Apps. Mehrere Quellen können gleichzeitig ausgewählt werden. Das Mikrofon bleibt unabhängig davon zuschaltbar.

Die Auswahl wird dauerhaft nach App-Namen gespeichert. Spezi verbindet später gestartete oder neu gestartete Apps automatisch wieder. Zu einer App gehörende Unterprozesse werden gemeinsam erfasst, überlappende Auswahlen werden nicht doppelt gemischt. Die Lautstärke lässt sich pro Quelle einstellen und während der Aufnahme ändern. Diese Änderung bleibt auch nach einem App-Neustart wirksam.

Beim aktuellen Spiel verwendet Spezi das ausgewählte Spielfenster oder das erkannte Vordergrundspiel bei Bildschirmaufnahme. Beim Wechsel zu Spezi bleibt das zuletzt erkannte laufende Spiel ausgewählt. Für eine fest bestimmte Anwendung kann stattdessen deren eigener Eintrag gewählt werden.

Audioquellen gelten ab der nächsten Aufnahme. Der Audiotest übernimmt geänderte Auswahlen sofort. Beginnt eine echte Aufnahme während eines Audiotests, beendet Spezi den Test und startet die Videoaufnahme korrekt. Alle aufgenommenen Quellen landen wie bisher gemeinsam in einer AAC-Tonspur.

App-spezifische Aufnahme verwendet die [Windows-Prozess-Loopback-Funktion](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ns-audioclientactivationparams-audioclient_process_loopback_params). Dafür ist Windows Build 20348 oder neuer nötig; normale Windows-Desktopinstallationen erfüllen das mit Windows 11. Ältere Systeme bieten weiterhin Systemton-Aufnahme an. App-Audio weicht bei einer nicht laufenden oder fehlerhaften Quelle niemals auf die Aufnahme des gesamten Systemtons aus.

## Prüfung

- Entwicklungsbuilds ohne Fehler und Warnungen; kein neuer Release-Build und kein neuer Installer.
- Bestehende Einstellungen, Speichern und Laden, alle 20 Zeitstufen und laufendes Verkleinern des komprimierten Puffers geprüft.
- Beide Slider, Punktgrößen, genaue Zeitanzeige, sofortiger Farbwechsel, App-Auswahl und unabhängige Mikrofonsteuerung in der tatsächlichen Oberfläche geprüft.
- Zwei gleichzeitig klingende Test-Apps verwendet: Die ausgewählte App samt Unterprozess ist im MP4 hörbar; die ausgeschlossene App auf demselben Audiogerät ist ausgeschlossen.
- Automatisches Anschließen nach App-Start und App-Neustart, Lautstärkeänderung, Audiotest → Replay, gespeicherter Replay-Clip und vollständige Aufnahme geprüft.
- Bestehende Systemton-Aufnahme einschließlich Unterbrechung und Wiederaufnahme des Testtons geprüft. Andere hörbare Apps beeinflussen diese Frequenzprüfung nicht.
- Benutzereinstellungen und vorhandene Release-Dateien bleiben unverändert.

Die Prüfnachweise liegen in `tests/results/replay-app-audio-20261009`. Bilder der tatsächlichen Oberfläche liegen in `previews/replay-slider-mint.png`, `previews/replay-slider-violet.png` und `previews/app-audio-settings.png`.

Technische Referenzen: [Microsofts Beispiel](https://github.com/microsoft/Windows-classic-samples/tree/main/Samples/ApplicationLoopback), [asynchrone Audioaktivierung](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-activateaudiointerfaceasync), [Audiopaket-Zeitstempel](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer).
