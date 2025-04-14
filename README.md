# STDISCM P4 – Miguel Kua, Kevin Tuco, Josh Ang Ngo Ching, Sidney Ting

## Network Setup

The following URLs are being used by the system:
```
http://localhost:5000 = Broker
http://localhost:5001 = View node
http://localhost:5002 = AuthController
http://localhost:5003 = CoursesController
http://localhost:5004 = GradesController
http://localhost:5006 = CoursesDb1
http://localhost:5008 = GradesDb1
http://localhost:5010 = UsersDb1
```

Modify the `localhost` portion of each hardcoded occurence of the URL with the IP address of the machine/VM that the node will be running on. It is recommended to use `Ctrl + Shift + F` on Visual Studio Code for this task.

Note: You may use any number of machines/VMs.

For example, in the [video demo](https://drive.google.com/file/d/1B1SYGSHqpPAQ3EhCcQE8xLxxy4Gl5Ytr/view?usp=sharing), the following setup was used:

```
Local machine (192.168.68.110) - Dashboard, View node
VM 1 (192.168.68.137) - Broker
VM 2 (192.168.68.132) - AuthController, CoursesController, GradesController
VM 3 (192.168.68.135) - UsersDB, CoursesDB, GradesDB
```

So, the URLs throughout the project were replaced as follows:
```
http://192.168.68.137:5000 = Broker
http://192.168.68.110:5001 = View node
http://192.168.68.132:5002 = AuthController
http://192.168.68.132:5003 = CoursesController
http://192.168.68.132:5004 = GradesController
http://192.168.68.135:5006 = CoursesDb1
http://192.168.68.135:5008 = GradesDb1
http://192.168.68.135:5010 = UsersDb1
```

In total, there should be roughly 14 URLs throughout the system that will be changed.


## How to Run

1. Open a terminal.
2. Go to the folder of each node. For example:

    ```bash
    cd AuthController-Node/AuthControllerNode
    ```

3. Then, run the service:

    ```bash
    dotnet run
    ```

4. Repeat for all other nodes.


## Startup Order

To avoid service dependency issues, it is recommended to start the nodes in this order:

1. `BrokerNode`
2. `DashboardNode`
3. `AuthControllerNode`
4. `CoursesControllerNode`
5. `GradesControllerNode`
6. `UsersDb1Node`
7. `CoursesDb1Node`
8. `GradesDb1Node`
9. `ViewNode`


## System Status Check

View the status of each node by visiting the **Dashboard** at: ```localhost:5138```. You can activate or deactive the nodes via checkboxes. Remember to press "Update" for the node's status to be applied.


## View Components

### Student's View
- View grades
- View all available courses
- Enroll in a course
- View enrolled courses

### Teacher's View
- Upload student grades

---

## Testing Credentials

Use the following accounts for testing:

- **Student Account**  
  `Username:` `student1`  
  `Password:` `pass1`

- **Teacher Account**  
  `Username:` `teacher101`  
  `Password:` `pass1`

These accounts are initialized in the `Users` database by default.
