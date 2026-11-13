# Teststand 13.11.2026 · Version 1.0.4

HTTP-HLS spielt jetzt direkt im Hauptfenster sowie in eingebetteten Playern und Quellenfenstern. Der separate Kompatibilitätsmodus wurde entfernt. HTTP- und HTTPS-Navigation funktionieren auch nach einem Wechsel des Ursprungs. Die Videoerkennung funktioniert zusätzlich auf öffentlichen HTTP-Seiten ohne die nur in sicheren Kontexten verfügbare Funktion `crypto.randomUUID`.

Die kontrollierte Browserprüfung `--mixed-content-only` besteht: tatsächliche HLS-Videodecodierung mit HTTP-Medien auf HTTPS-Seiten und in eingebetteten Frames, Übernahme derselben Unterstützung im Quellenfenster und fortgesetzte Wiedergabe nach HTTP-/HTTPS-Navigation. Lokaler Bericht: `verification/http-browser.json`.

Die Fensterprüfung `--popup-only` besteht einschließlich HTTP als Standard für Adressen ohne Protokoll, Quelle im eigenen Fenster, tatsächlicher VLC-Videodecodierung, `window.opener`, `window.close`, zunächst leeren Fenstern, manueller Öffnung automatisch blockierter Fenster und Weiterleitungen. Lokaler Bericht: `verification/popup-http-browser.json`.

Die unabhängigen MP4-/HLS-Quellenprüfungen bestehen ebenfalls. GitHub Actions erzeugt die Testmedien synthetisch und führt diese Prüfungen vor dem Windows-Build aus. WebView2- und VLC-Wiedergabetests sind lokale Prüfungen; die folgenden historischen Anbieterbefunde sind keine erneute Prüfung der jeweiligen externen Webseite am 13.11.2026.

## Historische Abnahmen vom 21.–22.10.2026

Windows-x64-Ausgabe, .NET SDK 10.0.402, WebView2 SDK 1.0.4258.31, installierter VLC 3.0.24.

## Quellenfenster (Version 1.0.3)

Der kontrollierte Test `--popup-only` besteht: Benutzeraktion öffnet ein vollständiges StreamBrowser-Fenster, `window.opener` und Referer bleiben erhalten, ein Video wird dort erkannt, geprüft und tatsächlich in VLC decodiert. `window.close()` schließt das Kindfenster. Ein zunächst leeres Fenster kann später seine Zieladresse erhalten. Eine automatisch von der Seite erzeugte Fensteranforderung wird blockiert und lässt sich über die App-Schaltfläche nachträglich öffnen. Ein Quellenfenster aus dem Kompatibilitätsmodus kann seine anfängliche Weiterleitung zu einem anderen Ursprung abschließen und bleibt anschließend auf diesen Ursprung begrenzt. Rohbericht: `verification/popup-result.json`.

Direkter Test von `https://vavoo.to/#/item/tmdb/video/623`: **Anschauen** lädt die Quellenliste. Die erste Quelle fordert `https://dood.to/e/uh3hjt99blns` als neues Fenster an. Das Fenster öffnet sich; dessen Weiterleitung zu playmogo.com wird durch Microsoft SmartScreen als unsicher blockiert. Die frühere pauschale App-Sperre des Seitenfensters ist damit beseitigt. Eine erfolgreiche Filmwiedergabe über diese erste Quelle wird nicht behauptet. Befund: `verification/item-popup-dood.json` und `verification/item-popup-dood.png`.

Die alternative Quelle **Server C1 · voe.sx** öffnet das Quellenfenster, folgt dessen JavaScript-Weiterleitung zu `derricknationtop.com` und erreicht die VOE-Seite für den ausgewählten Film. Das dortige Videoelement und die StreamBrowser-Erkennung sind vorhanden. Ein Abspielen des vollständigen Films wurde nicht geprüft. Befund: `verification/item-popup-voe.json` und `verification/item-popup-voe.png`.

## Direkter Test von vavoo.to (Version 1.0.2)

Auf `https://vavoo.to/#/channels` wurde mit **ZDF HD (1)** die gemeldete Fehlermeldung im normalen Browser reproduziert: HTTPS-Dokument, blockierter HTTP-Stream, kein decodiertes Videobild. Im separaten Kompatibilitätsfenster lief derselbe Sendereintrag mit HTTP-200-Antworten für HLS-Manifest und TS-Mediensegmente. Auch die fertige Ausgabe 1.0.2 wurde erneut geprüft: Der Browser decodierte zum Messzeitpunkt 14 Frames bei einer Wiedergabezeit von 13,9 Sekunden (`readyState=4`, nicht pausiert). Anschließend wurde eine VLC-Übergabe bestätigt: 2 decodierte Videobilder und 2 Audioblöcke.

Rohbericht der fertigen Ausgabe: `verification/vavoo-result.json`; Testbild: `verification/vavoo-compatible.png`; die reproduzierte normale Sperre steht in `verification/vavoo-normal.png`. Bei einem vorangehenden Versuch mit **ZDF HD+ (1)** antwortete eine Quelle mit HTTP 502. Die Webseite ordnet Fehler bei einer HTTP-Quelle unter HTTPS pauschal als „mixed“ ein; dieselbe Meldung kann dort daher auch einen weiteren Ladefehler überdecken. Es besteht keine Wiedergabegarantie für alle Sender und Quellvarianten.

Die App zeigt jetzt bei einer tatsächlichen Mixed-Content-Sperre einen eigenen Hinweis mit Schaltfläche zum Kompatibilitätsfenster. HLS-Anfragen werden bereits vor Empfang einer Antwort als Kandidaten erfasst, damit auch bei einer Browsersperre die unabhängige Quellenprüfung und VLC-Übergabe möglich bleiben.

## HTTP-Kompatibilität

Der Diagnoseaufruf mit `--mixed-content-only` ist erfolgreich: HTTP-HLS wird in einer HTTPS-Testseite und einem eingebetteten HTTPS-Player anderer Herkunft im normalen Browser gesperrt. Nach Öffnen des separaten Kompatibilitätsfensters werden in beiden Dokumenten HLS-Segmente per HTTP geladen und tatsächliche Videobilder decodiert. Navigation zu einer anderen Domain und eine Herabstufung des Hauptdokuments von HTTPS auf HTTP werden gesperrt. Das gleichzeitig geöffnete normale Fenster blockiert Mixed Content weiterhin.

Rohbericht: `verification/mixed-content.json`; Browserbild: `verification/mixed-content-playing.png`. Dies prüft die HTTP-/HTTPS-Blockade zusätzlich unter kontrollierten Bedingungen. Blockierte HTTP-HLS-Versuche bleiben für die Quellenprüfung erfasst. Der Kompatibilitätsmodus nutzt vorläufig ein WebView2-Browserflag; dessen Unterstützung durch künftige Runtime-Versionen ist nicht zugesichert.

## Bestanden

- MP4, HTTP-Weiterleitung, HLS-VOD, HLS-Live und HLS-Master-Playlist erkannt und geprüft.
- Erforderliche Referer- und User-Agent-Angaben für MP4 und HLS: sowohl Quellenprüfung als auch tatsächliche VLC-Wiedergabe erfolgreich; bei HLS einschließlich Segmentabrufen.
- VLC decodiert Video und Audio. Ein MP4 wird an der übergebenen Position ab Sekunde 5 gestartet.
- Grüne Umrandung und VLC-Schaltfläche; Videos werden auch unter darüberliegenden Player-Steuerelementen erkannt.
- Eingebetteter Frame mit anderer Herkunft: Erkennung, Nachrichtenübertragung und VLC-Wiedergabe erfolgreich.
- Mehrere MP4-Videos: nur das ausgewählte Video wird freigegeben. Mehrere HLS-Kandidaten: eine ausdrückliche Quellenauswahl ist erforderlich.
- Nach bestätigter VLC-Wiedergabe: Browservideo pausiert, Quelle entfernt, weitere zugehörige Abrufe gesperrt. Normale Seitenabrufe funktionieren weiterhin.
- Native Anforderungssperre mit einem Browser-Worker geprüft, der die JavaScript-Mediensperre umgeht.
- HTTP 403, HTML statt Video, fehlendes HLS-Segment, geschütztes HLS und zyklische Manifeste werden abgelehnt.
- Abbruch der Prüfung und fehlendes VLC werden behandelt. Kann VLC ein vermeintliches MP4 nicht decodieren, wird nach spätestens 15 Sekunden ein Fehler gemeldet; die Browserquelle bleibt erhalten und nur die fehlgeschlagene eigene VLC-Instanz wird beendet.
- Gefälschte Übergabenachricht ohne passenden Zielbezug und Freigabetoken wird ignoriert.

Die automatischen Browserprüfungen haben sechs tatsächliche VLC-Übergaben durchgeführt. Rohbericht: `verification/controlled.json`. Der zusätzliche Test mit einem realen hls.js-Player steht separat unter `verification/real-hls.json`.

## Beispielseite taraftarium.co

Startseite und eingebetteter Player laden; die Erkennung ist in beiden Dokumenten aktiv. Nach Start der vorgeschalteten Werbung und Betätigung der angebotenen Überspringen-Schaltfläche wurde eine HLS-Quelle erkannt und erfolgreich auf Manifest-/Segmentzugriff geprüft.

**Eine erfolgreiche VLC-Wiedergabe dieses konkreten Streams konnte im Test nicht bestätigt werden.** Die Übergabe meldete nach 15 Sekunden fehlende Wiedergabe; der Browserplayer blieb aktiv. Die konkrete Ursache ist nicht abschließend bestimmt. Die allgemeine MP4-/HLS-Unterstützung und dieser einzelne Anbieter müssen deshalb getrennt beurteilt werden. Es werden keine Sitzungscookies oder zusätzlichen Zugangskopfzeilen übernommen.

Der Frame-/Quellenbefund steht unter `verification/candidate-site.json`. Quellenabfragen und Serververhalten können sich ändern.

## Fertige Ausgabe

`release/win-x64-1.0.3/StreamBrowser.exe` starten und den vollständigen Ausgabeordner zusammenlassen. Die .NET-Laufzeit ist enthalten; WebView2 Runtime und der externe VLC bleiben erforderlich.

