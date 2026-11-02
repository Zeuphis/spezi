# Benachrichtigungen

Die Meldungen verwenden schmale dunkle Karten mit abgerundeten Ecken, einer mintgrünen Markierung und kleineren Symbolen für gespeicherte Videos, Replay und Aufnahme. Fehler erscheinen rot, Hinweise gelb. Clip-Meldungen zeigen nur die Dauer aus der Aufnahmebestätigung und den Dateinamen; der Galerie-Hinweis entfällt. Lange Namen werden gekürzt. Die übliche Höhe beträgt etwa 78 statt 144 Punkten; längere wichtige Hinweise können die Karte vergrößern. Replay zeigt eine kurze Angabe zum Puffer.

Die Karten stehen mit 20 Punkten Abstand oben rechts im Arbeitsbereich des Vordergrundbildschirms. Wenn Spezi selbst im Vordergrund steht, wird der ausgewählte Aufnahmebildschirm bzw. das Aufnahmefenster verwendet. Die Position wird in Bildschirmkoordinaten mit der DPI des Zielfensters gesetzt.

Die Benachrichtigungen aktivieren sich nicht, fangen keine Mausaktionen ab und verschwinden nach 3,5 Sekunden. Eine neue Meldung ersetzt die bestehende und startet die Anzeigedauer erneut. Es gibt keine dauerhafte Animation und kein laufendes Overlay bei aktivem Replay. Die Einstellung für Statusmeldungen wird weiterhin berücksichtigt. Es handelt sich um ein normales Windows-Fenster; Sichtbarkeit über exklusiven Vollbildspielen wurde nicht getestet.

Validierung: Release-Build ohne Fehler oder Warnungen; echter Fenstertest mit Fokusprüfung, nativer Position oben rechts, Fenstereigenschaften, Wiederverwendung, Ersetzen einer laufenden Meldung und automatischem Ausblenden. Die gerenderten Vorschauen für gespeicherten Clip, Replay und langen Dateinamen wurden visuell geprüft. Keine Aufnahme- oder Audioänderungen.
