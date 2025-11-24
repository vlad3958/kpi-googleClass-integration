# Інтеграція Google Classroom (Service Account + Domain‑Wide Delegation)

Цей репозиторій містить мінімальне API для роботи з Google Classroom:
– Масові інвайти вчителів та студентів на курс.
– Отримання списку курсів та їхніх ID.
– Пошук курсу за назвою.
– Оцінки студентів по одному курсу та агрегований звіт.

Документ призначений для адміністратора Google Workspace, який налаштовує доступ.

## 1. Попередні вимоги
1. Домен Google Workspace (Google Workspace for Education або Business).  
2. Обліковий запис із роллю Super Admin (`ber@vlad.work.gd` у прикладі).  
3. Доступ до Google Cloud Console (можна під тим самим адмін‑акаунтом).  

## 2. Створення проекту в Google Cloud Console
1. Зайдіть: https://console.cloud.google.com/ (під адміном).  
2. Створіть новий проект ("New Project") — назва, організація, локація.

## 3. Увімкнення потрібних API
У проекті перейдіть у "APIs & Services" → "Enable APIs and Services" та додайте:
– Google Classroom API

Переконайтесь, що Google Classroom API має статус "Enabled".

## 4. Створення Service Account
1. "IAM & Admin" → "Service Accounts" → "Create Service Account".  
2. Назва: `classroom-integration-sa`.  
3. Ролі можна не додавати (для початку достатньо базового створення).  
4. Після створення відкрийте сервісний акаунт → вкладка "Keys" → "Add Key" → "Create new key" → тип JSON. Збережіть файл `service-account.json` ПОЗА репозиторієм або у захищеному місці.
5. У властивостях увімкніть чекбокс "Enable G Suite Domain-wide Delegation" (може називатися "Enable Domain-wide Delegation").  
6. Збережіть **Client ID** сервісного акаунта (потрібен для делегації у адмін‑консолі).

## 5. Domain‑Wide Delegation в Admin Console
1. Зайдіть як супер‑адмін у Admin Console: https://admin.google.com/  
2. "Security" → "Access and data control" → "API controls".  
3. У блоці "Domain-wide Delegation" натисніть "Manage Domain Wide Delegation" → "Add new".  
4. Вставте **Client ID** сервісного акаунта.  
5. Додайте scopes (через кому або по одному):
```
https://www.googleapis.com/auth/classroom.courses
https://www.googleapis.com/auth/classroom.rosters
https://www.googleapis.com/auth/classroom.coursework.students
https://www.googleapis.com/auth/classroom.profile.emails
```
6. Збережіть. Тепер сервісний акаунт може імперсонувати адміністратора та діяти від його імені.


## 8. Основні ендпоінти (Service Account варіант)
Кореневий маршрут: `api/invitations`

| Метод | Шлях | Опис |
|-------|------|------|
| POST | `course/invite` | Масові інвайти: поле `course` (ID або посилання), списки `teacherEmails`, `studentEmails`. |
| POST | `create-full` | Створити курс + одразу додати вчителів і студентів (перший вчитель фіксований `ber@vlad.work.gd`). |
| GET  | `courses/ids` | Всі курси та їхні ID для імперсонованого адміна. |
| GET  | `courses/id-by-name?name=...` | Повертає ID курсу за назвою. |
| GET  | `courses/{courseId}/grades` | Детальні оцінки студентів у конкретному курсі. |
| GET  | `grades/summary` | Агрегований звіт: середній бал та кількість студентів по кожному курсу. |
| GET  | `grades/all` | Повна вибірка усіх курсів з деталями оцінок. |

### Приклад тіла запиту для масового інвайту
```json
{
  "course": "https://classroom.google.com/c/Mjc0NTY3ODI0NzAz",
  "teacherEmails": ["teacher1@vlad.work.gd", "teacher2@vlad.work.gd"],
  "studentEmails": ["stud1@vlad.work.gd", "stud2@vlad.work.gd"]
}
```