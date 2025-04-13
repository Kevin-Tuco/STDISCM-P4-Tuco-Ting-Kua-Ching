# 🧪 STDISCM P4 – Miguel Kua, Kevin Tuco, Josh Ang Ngo Ching, Sidney Ting

## 🚀 How to Run

1. Open a terminal.
2. Go to the folder of the node:

    ```bash
    cd AuthController-Node/AuthControllerNode
    ```

3. Run the service:

    ```bash
    dotnet run
    ```

4. Repeat for all other nodes.

---

## 📌 Recommended Startup Order

To avoid service dependency issues, start the nodes in this order:

1. `UsersDb1Node`
2. `GradesDb1Node`
3. `CoursesDb1Node`
4. `AuthControllerNode`
5. `BrokerNode`
6. `CoursesControllerNode`
7. `GradesControllerNode`
8. `DashboardNode`
9. `ViewNode`

---


## ✅ System Status Check

To ensure all nodes are activated, visit: ```localhost:5138```. This opens the **Dashboard**, where you can enable or disable nodes via checkboxes.

---

## 🎓 View Components

### 👨‍🎓 Student's View
- View grades
- View all available courses
- Enroll in a course
- View enrolled courses

### 👨‍🏫 Teacher's View
- Upload student grades

---

## 🔐 Testing Credentials

Use the following accounts for testing:

- **Student Account**  
  `Username:` `student1`  
  `Password:` `pass1`

- **Teacher Account**  
  `Username:` `teacher101`  
  `Password:` `pass1`

These accounts are initialized in the `Users` database by default.
