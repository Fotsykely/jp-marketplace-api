# JP — marketplace de seconde main (type Vinted)

Projet d'apprentissage .NET : API ASP.NET Core 10 + Entity Framework Core + PostgreSQL.

## Démarrer

Prérequis : [SDK .NET 10](https://dotnet.microsoft.com/download), Docker.

```bash
docker compose up -d          # PostgreSQL sur localhost:5432
dotnet tool restore           # installe dotnet-ef (outil local)
dotnet run --project src/JP.Api --launch-profile http
```

Au démarrage (en Development), les migrations sont appliquées et des données de démo sont insérées.

- UI pour tester l'API : http://localhost:5088/scalar
- Spec OpenAPI : http://localhost:5088/openapi/v1.json

## Ce qui existe déjà (étape 1)

| Endpoint | Rôle |
|---|---|
| `GET /api/categories` | Liste des catégories (hiérarchiques) |
| `GET /api/items?q=&categoryId=&minPrice=&maxPrice=&condition=&sort=&page=&pageSize=` | Catalogue filtré + paginé |
| `GET /api/items/{id}` | Détail d'une annonce |
| `POST /api/items` | Créer une annonce |
| `PUT /api/items/{id}` | Modifier (refusé si vendue) |
| `DELETE /api/items/{id}` | Supprimer |

## Structure

```
src/JP.Api/
  Entities/     User, Category, Item, ItemPhoto, enums (état, statut)
  Data/         AppDbContext (config EF Core), SeedData
  Dtos/         Requêtes/réponses de l'API
  Controllers/  ItemsController, CategoriesController
  Migrations/   Générées par `dotnet ef`
```

## Commandes EF Core utiles

```bash
cd src/JP.Api
dotnet ef migrations add NomDeLaMigration   # après avoir modifié une entité
dotnet ef database update                   # appliquer à la main
dotnet ef migrations remove                 # annuler la dernière (si pas appliquée)
```

## Roadmap

**Étape 2 — Comptes et sécurité**
- [ ] ASP.NET Core Identity + JWT (inscription, connexion)
- [ ] `SellerId` lu depuis le token, plus dans le body
- [ ] Seul le vendeur peut modifier/supprimer son annonce
- [ ] Profil public d'un membre + ses annonces

**Étape 3 — Le cœur de Vinted**
- [ ] Upload de photos (`IFormFile`, stockage local puis Azure Blob / S3)
- [ ] Favoris
- [ ] Messagerie acheteur ↔ vendeur en temps réel avec **SignalR**
- [ ] Faire une offre / négocier le prix

**Étape 4 — Achat**
- [ ] Commande : `Available → Reserved → Sold` (machine à états)
- [ ] Paiement Stripe en mode test + webhook idempotent
- [ ] Libération auto de la réservation après 30 min (`BackgroundService`)
- [ ] Avis/notes après la vente

**Étape 5 — Qualité pro**
- [ ] Tests xUnit + Testcontainers (vrai PostgreSQL en test)
- [ ] CI GitHub Actions (build + tests)
- [ ] Dockerfile de l'API + déploiement
- [ ] Front Next.js (catalogue, fiche article, publier une annonce)

## Repères pour qui vient de Spring Boot

| Spring Boot | .NET |
|---|---|
| `@RestController` | `[ApiController]` + `ControllerBase` |
| JPA / Hibernate | Entity Framework Core |
| `JpaRepository` | `DbSet<T>` + LINQ |
| Flyway / Liquibase | `dotnet ef migrations` |
| `@Valid` + Bean Validation | Data annotations (validation auto via `[ApiController]`) |
| `@Autowired` / DI | Injection par constructeur, `builder.Services.Add...` |
| `application.yml` | `appsettings.json` |
| Spring Security | ASP.NET Core Identity / JWT Bearer |
| `@Scheduled` | `BackgroundService` |
| WebSocket / STOMP | SignalR |
| JUnit | xUnit |
