var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Content("""
<!doctype html>
<html lang="fr">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>FileFlow Monitor</title></head>
<body>
  <main>
    <h1>FileFlow Monitor</h1>
    <p>Socle initial créé. Le tableau de bord Blazor sera implémenté dans la phase interface.</p>
  </main>
</body>
</html>
""", "text/html"));

app.Run();
