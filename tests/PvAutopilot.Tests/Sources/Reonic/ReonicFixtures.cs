namespace PvAutopilot.Tests.Sources.Reonic;

/// <summary>
/// API-Antworten nach dem Schema der Reonic-OpenAPI-Spezifikation v3 (inkl. Feldern, die der Adapter nicht liest).
/// Nur fiktive Daten.
/// </summary>
internal static class ReonicFixtures
{
    public const string ProjectId = "11111111-1111-4111-8111-111111111111";
    public const string VariantId = "22222222-2222-4222-8222-222222222222";

    public static string Project(string? primaryVariantId = VariantId, string? meterNumber = " 1ESY0000000099 ") => $$"""
        {
          "id": "{{ProjectId}}",
          "name": "Testprojekt",
          "stage": "offer",
          "latLng": { "lat": 51.43, "lng": 6.76 },
          "address": { "street": "Musterstraße", "houseNumber": "1", "zip": "47051", "city": "Duisburg", "country": "DE" },
          "customerContact": null,
          "primaryOfferVariantId": {{Json(primaryVariantId)}},
          "offerVariantIds": ["{{VariantId}}"],
          "variantIds": ["{{VariantId}}"],
          "existingSystems": { "solar": null, "heating": null, "batteryStorage": null, "evCharger": null },
          "offerNumber": "A-2026-001",
          "meterNumber": {{Json(meterNumber)}},
          "integrations": { "hubspotDealId": "123456" }
        }
        """;

    public static string Variant(bool bundleSelected = true) => $$"""
        {
          "id": "{{VariantId}}",
          "name": "Hauptvariante",
          "isPrimary": true,
          "totalPrice": { "net": 20000, "gross": 20000, "vat": 0 },
          "totalPriceOverride": null,
          "systems": {
            "solar": {
              "net": 15000, "gross": 15000, "vat": 0,
              "lineItems": [
                {{LineItem("comp-module", "ver-module", "module", "Modul 450", "Hersteller A", 12)}},
                {{LineItem("comp-module", "ver-module", "module", "Modul 450", "Hersteller A", 8)}},
                {{LineItem("comp-inverter", "ver-inverter", "inverter", "WR 8K", "Hersteller B", 1)}},
                {{LineItem("comp-optimizer", "ver-optimizer", "optimizer", "Optimierer", "Hersteller G", 20)}}
              ]
            },
            "batteryStorage": {
              "net": 5000, "gross": 5000, "vat": 0,
              "lineItems": [ {{LineItem("comp-battery", "ver-battery", "batteryStorage", "Speicher 2.56", "Hersteller C", 4)}} ]
            },
            "heating": { "net": 0, "gross": 0, "vat": 0, "lineItems": [] },
            "evCharger": { "net": 0, "gross": 0, "vat": 0, "lineItems": [] },
            "additional": { "net": 0, "gross": 0, "vat": 0, "lineItems": [] }
          },
          "optionalBundles": [
            {
              "id": "bundle-wallbox", "name": "Wallbox", "description": null, "selected": {{(bundleSelected ? "true" : "false")}},
              "net": 1000, "gross": 1000, "vat": 0,
              "lineItems": [ {{LineItem("comp-wallbox", "ver-wallbox", "evCharger", "Wallbox 11", "Hersteller D", 1)}} ]
            },
            {
              "id": "bundle-extra-battery", "name": "Zweiter Speicher", "description": null, "selected": false,
              "net": 3000, "gross": 3000, "vat": 0,
              "lineItems": [ {{LineItem("comp-battery-2", "ver-battery-2", "batteryStorage", "Speicher XL", "Hersteller C", 1)}} ]
            }
          ]
        }
        """;

    private static string LineItem(string componentId, string versionId, string type, string name, string brand, decimal quantity) => $$"""
        {
          "componentId": "{{componentId}}", "componentVersionId": "{{versionId}}", "componentType": "{{type}}",
          "name": "{{name}}", "description": null, "brand": "{{brand}}", "articleNumber": null, "gtin": null,
          "quantity": {{quantity}}, "visibleToCustomer": true,
          "unitSalesPrice": { "net": 1, "gross": 1, "vat": 0, "vatRate": 0 },
          "totalSalesPrice": { "net": 1, "gross": 1, "vat": 0 },
          "unitPurchasePrice": null, "totalPurchasePrice": null, "purchasePriceOverridden": false,
          "margin": { "total": 0, "rate": null }
        }
        """;

    public static string Component(string id, string versionId, string type, string name, string brand, string attributes) => $$"""
        {
          "id": "{{id}}", "versionId": "{{versionId}}", "name": "{{name}}", "description": null, "brand": "{{brand}}",
          "salesPrice": 100, "vatRate": 0, "purchasePrice": null, "quantityUnit": "item", "imageUrl": null,
          "datasheetUrl": "https://files.example/{{id}}.pdf", "warrantyUrl": null, "instructionsUrl": null,
          "createdAt": "2026-01-01T00:00:00.000Z", "updatedAt": null, "archivedAt": null,
          "componentType": "{{type}}",
          "attributes": {{attributes}}
        }
        """;

    public static readonly IReadOnlyDictionary<string, string> Components = new Dictionary<string, string>
    {
        ["components/comp-module/versions/ver-module"] = Component("comp-module", "ver-module", "module", "Modul 450", "Hersteller A",
            """{ "moduleType": "SiMono", "wattPeak": 450, "bypassDiodeCount": 3, "width": 1134, "height": 1722, "depth": 30, "weight": 21.5 }"""),
        ["components/comp-inverter/versions/ver-inverter"] = Component("comp-inverter", "ver-inverter", "inverter", "WR 8K", "Hersteller B",
            """{ "nominalOutputPower": 8000, "outputVoltage": 400, "nominalInputPower": 12000, "efficiency": 98.2, "numberOfPhases": 3 }"""),
        ["components/comp-battery/versions/ver-battery"] = Component("comp-battery", "ver-battery", "batteryStorage", "Speicher 2.56", "Hersteller C",
            """{ "capacity": 2560, "maxChargingPower": 5000, "maxDischargingPower": 5000, "chargingEfficiency": null, "dischargingEfficiency": null, "numberOfPhases": 3 }"""),
        ["components/comp-wallbox/versions/ver-wallbox"] = Component("comp-wallbox", "ver-wallbox", "evCharger", "Wallbox 11", "Hersteller D",
            """{ "maxChargingPower": 11000, "cableLength": 5, "hasApp": true }"""),
        ["components/comp-optimizer/versions/ver-optimizer"] = Component("comp-optimizer", "ver-optimizer", "optimizer", "Optimierer", "Hersteller G", "{}"),
    };

    public static string Checklist(string zaehlerAnzahl = "1") => $$"""
        {
          "stage": "offer",
          "version": 3,
          "updatedAt": "2026-11-02T10:00:00.000Z",
          "updatedById": "user-1",
          "blocks": [
            {
              "id": "block-allgemein", "name": "Beratung", "position": 0, "backgroundColor": "#ffffff", "textColor": "#000000", "versionHash": "a1",
              "segments": [
                {
                  "id": "seg-dach", "name": "Dach", "position": 0, "backgroundColor": "#ffffff", "textColor": "#000000", "versionHash": "a2",
                  "completedAt": null, "completedById": null,
                  "items": [
                    { "type": "title", "title": "Dach" },
                    { "type": "radio", "title": "Dachneigung", "description": null, "required": false,
                      "items": [ { "text": "30°", "checked": true } ], "markedIrrelevant": false, "markedIrrelevantReason": null },
                    { "type": "plannedLayout", "title": "Belegung" }
                  ]
                }
              ]
            },
            {
              "id": "block-na", "name": "Netzanmeldung", "position": 1, "backgroundColor": "#ffffff", "textColor": "#000000", "versionHash": "b1",
              "segments": [
                {
                  "id": "seg-za", "name": "NA – Zähleranlage", "position": 0, "backgroundColor": "#ffffff", "textColor": "#000000", "versionHash": "b2",
                  "completedAt": "2026-11-02T10:00:00.000Z", "completedById": "user-1",
                  "items": [
                    { "type": "image", "title": "Fotos Zähleranlage", "description": null, "markedIrrelevant": false, "markedIrrelevantReason": null,
                      "items": [
                        {{Photo("Foto Hauptstromzähler (Nummer lesbar)", "na.foto.hauptzaehler", "image", "https://files.example/hz.jpg")}},
                        {{Photo("Foto Zählerschrank (geöffnet, komplett)", "na.foto.zaehlerschrank", "image", "https://files.example/zs.jpg")}},
                        {{Photo("Foto Hausanschlusskasten (HAK) mit Sicherungen", "na.foto.hak", "image", "https://files.example/hak.jpg")}}
                      ] },
                    {{Radio("Kaskadenschaltung", "na.kaskade", "Nein", "Ja", "Nein")}},
                    {{Radio("Anzahl Zähler mit Wechsel, Abmeldung oder Neusetzung", "na.zaehler.anzahl", zaehlerAnzahl, "0", "1", "2", "3", "4")}},
                    { "type": "signature", "title": "Unterschrift Monteur", "description": null, "required": false, "locksSegment": false,
                      "image": null, "signingPersonType": null, "signingPersonName": null, "signedAt": null,
                      "markedIrrelevant": false, "markedIrrelevantReason": null }
                  ]
                },
                {
                  "id": "seg-z1", "name": "NA – Zähler 1", "position": 1, "backgroundColor": "#ffffff", "textColor": "#000000", "versionHash": "b3",
                  "completedAt": null, "completedById": null,
                  "items": [
                    {{Radio("Zähler 1: Typ Zählerwechsel", "na.zaehler1.typ", "Tausch gegen Zweirichtungszähler", "Tausch gegen Zweirichtungszähler", "Ausbau – Zähler abmelden", "Neusetzung zusätzlicher Zähler", "Umbau auf Kaskade (Erzeugungszähler)", "Kein Wechsel (nur Bestand)")}},
                    {{Text("Zähler 1: Zählernummer", "na.zaehler1.nummer", "1ESY0000000099")}},
                    {{Radio("Zähler 1: Lage der Verbrauchsstelle", "na.zaehler1.verbrauchsstelle", "Wohnung", "Wohnung", "Allgemeinstrom (Haus)")}},
                    {{Text("Zähler 1: Bezeichnung Verbrauchsstelle", "na.zaehler1.verbrauchsstelle.text", null)}},
                    {{Radio("Zähler 1: Einbauort", "na.zaehler1.einbauort", "Keller", "Keller", "Flur")}},
                    {{Radio("Zähler 1: Befestigung", "na.zaehler1.befestigung", "3-Punkt", "3-Punkt", "eHZ", "Unbekannt")}}
                  ]
                },
                {
                  "id": "seg-anlage", "name": "NA – Anlage", "position": 2, "backgroundColor": "#ffffff", "textColor": "#000000", "versionHash": "b4",
                  "completedAt": null, "completedById": null,
                  "items": [
                    {{Radio("Neuerrichtung oder Erweiterung", "na.anlage.art", "Neuerrichtung", "Neuerrichtung", "Erweiterung einer bestehenden Anlage")}},
                    {{Radio("Messkonzept", "na.messkonzept", "Überschusseinspeisung – Zweirichtungszähler", "Überschusseinspeisung – Zweirichtungszähler", "Sonstiges (siehe Bemerkung)")}},
                    {{Text("Bemerkung Messkonzept", "na.messkonzept.bemerkung", null)}}
                  ]
                },
                {
                  "id": "seg-unterlagen", "name": "NA – Unterlagen", "position": 3, "backgroundColor": "#ffffff", "textColor": "#000000", "versionHash": "b5",
                  "completedAt": null, "completedById": null,
                  "items": [
                    { "type": "image", "title": "Unterlagen", "description": null, "markedIrrelevant": false, "markedIrrelevantReason": null,
                      "items": [ {{Photo("Übersichtsschaltplan", "na.schaltplan", "pdf", "https://files.example/schaltplan.pdf")}} ] },
                    { "type": "circuitPlan", "title": "Schaltplan" },
                    { "type": "datasheets", "title": "Datenblätter", "datasheetItemsToShow": null, "warrantyItemsToShow": null, "instructionItemsToShow": null },
                    { "type": "componentList", "title": "Komponenten" }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private static string Photo(string title, string key, string type, string url) => $$"""
        { "title": "{{title}}", "description": "Bitte fotografieren.\n[key: {{key}}]", "required": false, "maxAttachments": 2,
          "attachments": [ { "type": "{{type}}", "url": "{{url}}" } ], "exampleImage": null, "allowedAttachmentType": "Both",
          "markedIrrelevant": false, "markedIrrelevantReason": null }
        """;

    private static string Radio(string title, string key, string selected, params string[] options) => $$"""
        { "type": "radio", "title": "{{title}}", "description": "[key: {{key}}]", "required": false,
          "items": [ {{string.Join(", ", options.Select(o => $$"""{ "text": "{{o}}", "checked": {{(o == selected ? "true" : "false")}} }"""))}} ],
          "markedIrrelevant": false, "markedIrrelevantReason": null }
        """;

    private static string Text(string title, string key, string? text) => $$"""
        { "type": "freetext", "title": "{{title}}", "description": "[key: {{key}}]", "required": false, "rows": 1,
          "text": {{Json(text)}}, "markedIrrelevant": false, "markedIrrelevantReason": null }
        """;

    private static string Json(string? value) => value is null ? "null" : $"\"{value}\"";
}
