# Einstellungen, Spielnamen und Wiederherstellung – 6. Oktober 2026

## Einstellungen

Der Einstellungs-Tab verwendet abgesetzte Karten für Speicher/Quelle, Bildqualität, Hotkeys, Ton und Hintergrundverhalten. Qualitätsprofile zeigen ihre Zielwerte und ihren Auswahlzustand. Die Schalter und die Anordnung der Audiogeräte wurden vereinheitlicht. Es gibt einen ausdrücklichen Schalter für Systemton; das Abwählen des letzten Ausgabegeräts schaltet ihn aus. Hotkeys sind im schmalen Standardfenster vollständig lesbar.

Die Gestaltung wurde in der echten WPF-App gerendert und visuell geprüft. Die Vorschauen liegen unter `previews/settings-*.png`. Profilwahl, benutzerdefinierte Auflösung und Systemton-Schalter wurden mit getrennten Testeinstellungen geprüft. Die gespeicherten Benutzereinstellungen blieben unverändert.

## Spielnamen

Clips und Aufnahmen erhalten den erkannten Spielnamen, zum Beispiel `Overwatch_Clip_2026-10-06_...mp4` oder `Rainbow Six Siege_Aufnahme_2026-10-06_...mp4`. Namen werden aus dem aufgenommenen Fenster bzw. dem Vordergrundprozess abgeleitet. Bekannte Prozesse bekommen lesbare Namen; andere Bildschirmfüllende Anwendungen verwenden ihren Fenstertitel. Browser, Spezi, Launcher und übliche Desktopanwendungen werden ausgeschlossen.

Spezi beobachtet das Spiel auch bei manuell eingeschaltetem Rückblick alle zwei Sekunden. Beim Speichern per Hotkey wird zusätzlich sofort geprüft. Ein zuletzt im Rückblick beobachtetes Spiel bleibt für einen passenden Clip verfügbar, wenn zum Speichern kurz zu Spezi gewechselt wurde. Alte Spielnamen außerhalb des Rückblick-Zeitfensters werden verworfen. Eine vollständige Aufnahme behält das erste erkannte Spiel als Titel. Der Titel wird bei der laufenden Aufnahme für eine mögliche Wiederherstellung hinterlegt.

## Warum „Wiederhergestellt“ wiederholt erschien

Im lokalen Sitzungsordner liegt eine unterbrochene Aufnahme vom 5. Oktober 2026 (`20261005_225704`) mit einem beschädigten letzten MP4-Teil. Die alte Wiederherstellung schrieb direkt in den Galerieordner und scheiterte danach an diesem Teil. Dadurch erschien eine unvollständige Datei. Der Sitzungsordner blieb erhalten und wurde bei jedem Start erneut bearbeitet. Zusätzlich konnte dieselbe erfolgreiche Wiederherstellung zwei Meldungen auslösen.

Die Wiederherstellung prüft jetzt die Teile einzeln, setzt die lesbaren Teile in einer privaten temporären Datei zusammen und veröffentlicht erst ein geprüftes Video. Eine bestehende unvollständige Wiederherstellungsdatei wird vorher gesichert. Originalteile bleiben erhalten. Ein Fingerabdruck und ein Zustandsmarker verhindern erneute Kopien bei unveränderten Teilen; neue oder geänderte Originalteile können später erneut geprüft werden. Eine Veröffentlichung ist auch bei einer Unterbrechung zwischen Dateiverschiebung und Abschlussmarker idempotent. Die App zeigt nur eine Erfolgsmeldung.

## Prüfung

- Namensauflösung für Overwatch und Rainbow Six Siege, Ausschluss eines Vollbild-Browsers, Fenstertitel für unbekannte Vollbildquellen, sichere Dateinamen.
- Tatsächliche parallele Clips und eine 12-Sekunden-Aufnahme mit `Overwatch` im Dateinamen bei 1080p/60 FPS: 60 kodierte Bilder/s, keine verworfenen Bilder. Weitere Aufnahme- und Speicherfehlerprüfungen bestanden.
- Wiederherstellung aus zwei lesbaren Teilen und einem beschädigten letzten Teil: abspielbare Ausgabe, Sicherung der bisherigen unvollständigen Datei, keine Wiederholung und keine doppelten Dateien. Originalteile bleiben unverändert erhalten. Ein später ausgetauschter beschädigter Teil wird erneut erkannt.
- WPF-Einstellungen: Profile, Auflösung und Systemton-Schalter funktionieren; gespeicherte Benutzereinstellungen bleiben unverändert.

Es wurde kein echtes Overwatch- oder Rainbow-Six-Spiel gestartet. Die Aufnahmeprüfung verwendet Bildschirmaufnahmen mit einer simulierten Spielzuordnung. Berichte liegen unter `tests/results`. Frühere Quell- und Programmdateien sind unter `.backups/settings-update-*` gesichert. Im Projekt bleibt nur ein `dist`-Ordner.
