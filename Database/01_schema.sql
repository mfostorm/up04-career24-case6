-- УП.04, кейс 2, вариант 24. Схема кадрового агентства «Карьера»
CREATE DATABASE IF NOT EXISTS career24_up04
    CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
USE career24_up04;

CREATE TABLE IF NOT EXISTS roles (
    id TINYINT UNSIGNED NOT NULL AUTO_INCREMENT,
    code VARCHAR(20) NOT NULL,
    title VARCHAR(40) NOT NULL,
    CONSTRAINT pk_roles PRIMARY KEY (id),
    CONSTRAINT uq_roles_code UNIQUE (code)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS users (
    id INT UNSIGNED NOT NULL AUTO_INCREMENT,
    login VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    full_name VARCHAR(100) NOT NULL,
    password_hash BINARY(32) NOT NULL,
    password_salt BINARY(16) NOT NULL,
    password_iterations INT UNSIGNED NOT NULL,
    role_id TINYINT UNSIGNED NULL,
    is_active BOOLEAN NOT NULL DEFAULT FALSE,
    registered_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_users PRIMARY KEY (id),
    CONSTRAINT uq_users_login UNIQUE (login),
    CONSTRAINT fk_users_roles FOREIGN KEY (role_id) REFERENCES roles (id)
        ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_users_activation CHECK (is_active IN (0, 1) AND (is_active = 0 OR role_id IS NOT NULL)),
    CONSTRAINT ck_users_iterations CHECK (password_iterations BETWEEN 100000 AND 2000000)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS employers (
    id INT UNSIGNED NOT NULL AUTO_INCREMENT,
    name VARCHAR(120) NOT NULL,
    city VARCHAR(60) NOT NULL,
    contact_name VARCHAR(100) NOT NULL,
    contact_phone VARCHAR(20) NOT NULL,
    CONSTRAINT pk_employers PRIMARY KEY (id),
    CONSTRAINT uq_employers_city_name UNIQUE (city, name)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS vacancies (
    id INT UNSIGNED NOT NULL AUTO_INCREMENT,
    employer_id INT UNSIGNED NOT NULL,
    title VARCHAR(100) NOT NULL,
    city VARCHAR(60) NOT NULL,
    min_experience TINYINT UNSIGNED NOT NULL,
    salary_from DECIMAL(10,2) NOT NULL,
    requirements VARCHAR(400) NOT NULL,
    status ENUM('В работе','Приостановлена','Закрыта') NOT NULL,
    CONSTRAINT pk_vacancies PRIMARY KEY (id),
    CONSTRAINT fk_vacancies_employers FOREIGN KEY (employer_id) REFERENCES employers (id)
        ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_vacancies_experience CHECK (min_experience <= 60),
    CONSTRAINT ck_vacancies_salary CHECK (salary_from >= 0)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS candidates (
    id INT UNSIGNED NOT NULL AUTO_INCREMENT,
    full_name VARCHAR(100) NOT NULL,
    desired_title VARCHAR(100) NOT NULL,
    city VARCHAR(60) NOT NULL,
    experience TINYINT UNSIGNED NOT NULL,
    phone VARCHAR(20) NULL,
    email VARCHAR(128) NULL,
    consent BOOLEAN NOT NULL,
    CONSTRAINT pk_candidates PRIMARY KEY (id),
    CONSTRAINT uq_candidates_phone UNIQUE (phone),
    CONSTRAINT uq_candidates_email UNIQUE (email),
    CONSTRAINT ck_candidates_contact CHECK (phone IS NOT NULL OR email IS NOT NULL),
    CONSTRAINT ck_candidates_experience CHECK (experience <= 60),
    CONSTRAINT ck_candidates_consent CHECK (consent IN (0, 1)),
    INDEX ix_candidates_selection (desired_title, city, experience)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS recruitments (
    id INT UNSIGNED NOT NULL AUTO_INCREMENT,
    vacancy_id INT UNSIGNED NOT NULL,
    candidate_id INT UNSIGNED NOT NULL,
    status ENUM('На рассмотрении','Отложен','Завершён') NOT NULL,
    CONSTRAINT pk_recruitments PRIMARY KEY (id),
    CONSTRAINT uq_recruitments_pair UNIQUE (vacancy_id, candidate_id),
    CONSTRAINT fk_recruitments_vacancies FOREIGN KEY (vacancy_id) REFERENCES vacancies (id)
        ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT fk_recruitments_candidates FOREIGN KEY (candidate_id) REFERENCES candidates (id)
        ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS interviews (
    id INT UNSIGNED NOT NULL AUTO_INCREMENT,
    recruitment_id INT UNSIGNED NOT NULL,
    scheduled_at DATETIME NOT NULL,
    candidate_confirmed BOOLEAN NOT NULL,
    employer_confirmed BOOLEAN NOT NULL,
    status ENUM('Назначено','Проведено','Отменено') NOT NULL,
    CONSTRAINT pk_interviews PRIMARY KEY (id),
    CONSTRAINT uq_interviews_slot UNIQUE (recruitment_id, scheduled_at),
    CONSTRAINT fk_interviews_recruitments FOREIGN KEY (recruitment_id) REFERENCES recruitments (id)
        ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_interviews_confirmation CHECK (
        candidate_confirmed IN (0, 1) AND employer_confirmed IN (0, 1)
        AND (status = 'Отменено' OR (candidate_confirmed = 1 AND employer_confirmed = 1))
    )
) ENGINE=InnoDB;
