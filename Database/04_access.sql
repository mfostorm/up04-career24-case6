-- Выполняет владелец отдельного экземпляра кейса 5.
USE career24_up04;
CREATE ROLE IF NOT EXISTS 'c5_r_identity', 'c5_r_manager', 'c5_r_admin', 'c5_r_backup';

GRANT SELECT (login, full_name, password_hash, password_salt, password_iterations, role_id, is_active),
    INSERT (login, full_name, password_hash, password_salt, password_iterations)
    ON career24_up04.users TO 'c5_r_identity';
GRANT SELECT ON career24_up04.roles TO 'c5_r_identity';

GRANT SELECT, INSERT, UPDATE, DELETE ON career24_up04.employers TO 'c5_r_manager';
GRANT SELECT, INSERT, UPDATE, DELETE ON career24_up04.vacancies TO 'c5_r_manager';
GRANT SELECT, INSERT, UPDATE, DELETE ON career24_up04.candidates TO 'c5_r_manager';
GRANT SELECT, INSERT, UPDATE, DELETE ON career24_up04.recruitments TO 'c5_r_manager';
GRANT SELECT, INSERT, UPDATE, DELETE ON career24_up04.interviews TO 'c5_r_manager';

GRANT SELECT (id, login, full_name, role_id, is_active, registered_at)
    ON career24_up04.users TO 'c5_r_admin';
GRANT SELECT ON career24_up04.roles TO 'c5_r_admin';

DROP PROCEDURE IF EXISTS career24_up04.activate_manager;
DELIMITER //
CREATE PROCEDURE career24_up04.activate_manager(IN target_id INT UNSIGNED)
SQL SECURITY DEFINER
BEGIN
    UPDATE users u JOIN roles r ON r.code='manager'
    SET u.role_id=r.id, u.is_active=TRUE
    WHERE u.id=target_id AND u.is_active=FALSE AND u.role_id IS NULL;
    SELECT ROW_COUNT() AS activated;
END//
DELIMITER ;
GRANT EXECUTE ON PROCEDURE career24_up04.activate_manager TO 'c5_r_admin';

GRANT SELECT ON career24_up04.* TO 'c5_r_backup';
