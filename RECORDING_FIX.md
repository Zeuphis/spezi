# Aufnahme-Korrektur – 6. Oktober 2026

Die aktuelle App liegt unter `dist/Spezi.exe`. Vor dem Start Spezi vollständig über das Tray-Menü beenden, da nur eine App-Instanz gleichzeitig geöffnet werden kann. Die bisherigen Programmdateien wurden vor der Reparatur gesichert.

Behoben wurden doppelte AAC-Zeitstempel, ein unvollständig geleerter Audiopuffer, fehlende Videobilder auf ruhigen Aufnahmequellen und verlorene Bilder/Ton beim Zusammenfügen von Aufnahmeteilen.

Die Bildaufnahme arbeitet jetzt mit einem eigenen präzisen Takt und verwendet auf unveränderten Szenen das letzte GPU-Bild weiter. Ausgegebene Encoder-Samples werden freigegeben. Zeitstempel aus optionalen Media-Foundation-Attributen werden nur bei erfolgreichem Lesen übernommen. Bild- und Tonpakete werden vor einem Dateiteilwechsel nach ihren Zeitstempeln geordnet.

Die Codec-Steuerung verwendet den nativen COM-Zugriff wie der Transform selbst. Dadurch benötigt der Wechsel vom WPF-Fensterthread zum Encoder-Worker keinen vom Treiber nicht bereitgestellten COM-Proxy. Die fertig veröffentlichte WPF-App wurde zusätzlich mit ihrem eingebauten Selbsttest geprüft: Clip und Sitzung enthalten Bild und Ton, keine verworfenen Bilder, SELFTEST_OK.

Clips erhalten eigene Dateinamen und werden erst nach erfolgreichem Abschluss in den Speicherordner verschoben. Speicherprüfungen kollidieren nicht mehr bei parallelen Clips. Eine fehlgeschlagene Sitzung meldet keinen Erfolg. Wartende Encoder-Pakete werden vor dem Abschluss einer eigenständigen Aufnahme geleert. Decoder-Padding wird bei Galerie-Vorschaubildern berücksichtigt.

Geprüft auf dem vorhandenen NVIDIA-Hardware-Encoder mit Bildschirmaufnahmen in 1920 × 1080 bei 60 FPS:

- Zwei gleichzeitig gespeicherte Rückblick-Clips, jeweils mit Bild und Ton.
- Zwölf Sekunden Sitzung mit mehreren Dateiteilen: 722 Videobilder, 562 Audiopakete; Bilddauer 12,033 s, Tondauer 11,992 s. Größter Abstand der Videobilder 18,5 ms, der Audiopakete 21,3 ms. Der laufende Encoder erreichte 60 FPS ohne verworfene Bilder.
- Eigenständige Aufnahme und Aufnahme ohne Ton.
- Fehlerhafter Speicherort meldet einen Fehler und keinen Erfolg.
- AAC mit 960 und 9600 Eingangssamples pro Block: kontinuierliche Zeitstempel.
- Rückblickpuffer nach Ablauf seines Zeitfensters und QPC-Umrechnung nach vier Tagen Laufzeit.
- Zusammenfügen eines Testclips mit sich selbst erhält alle 476 Videobilder.

Die Prüfung verwendet temporäre Aufnahmeordner und verändert die App-Einstellungen nicht. Ein konkretes Spiel wurde dabei nicht aufgenommen.

Tests ausführen:

```powershell
dotnet run --project tests/Spezi.RecordingTests.csproj -c Release -- --60fps
```

Die bisherigen Quell- und Programmdateien sind vor der Übernahme unter `.backups/recording-fix-*` gesichert.
